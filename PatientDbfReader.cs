using System.Buffers.Binary;
using System.Text;

namespace ClinicCameraCapture;

/// <summary>
/// Read-only, in-memory NUM and ID lookup for FoxPro DBF.
/// Never opens CDX/FPT, never uses an xBase engine, never modifies an HIS file.
/// Only handles character / numeric fields needed by PD011M1.
/// </summary>
public sealed class PatientDbfReader
{
    public sealed record Patient(string Number, string Name, string Id)
    {
        public string DisplayId => string.IsNullOrWhiteSpace(Id) ? "0000" + Number : Id;
    }

    public sealed class Snapshot
    {
        private readonly Dictionary<string, Patient> _byNumber;
        private readonly Dictionary<string, Patient> _byId;
        private readonly HashSet<string> _duplicateIds;

        internal Snapshot(Dictionary<string, Patient> byNumber,
            Dictionary<string, Patient> byId, HashSet<string> duplicateIds)
        {
            _byNumber = byNumber;
            _byId = byId;
            _duplicateIds = duplicateIds;
        }

        public Patient? FindByNumber(string number)
        {
            if (!CaptureIdentity.IsValid(number)) return null;
            return _byNumber.GetValueOrDefault(CaptureIdentity.Require(number));
        }

        public Patient? FindById(string id)
        {
            var key = id.Trim().ToUpperInvariant();
            // Duplicate identifiers cannot resolve safely to one patient.
            if (_duplicateIds.Contains(key))
                throw new InvalidDataException("同一身分證號對應到多筆不同病歷，請改用病歷號查詢。");
            return _byId.GetValueOrDefault(key);
        }
    }

    private sealed record DbfField(string Name, char Type, int Offset, int Length);

    private static readonly Encoding Chinese;
    static PatientDbfReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Chinese = Encoding.GetEncoding(950);
    }

    public static Snapshot Load(string path)
    {
        if (!Path.GetFileName(path).Equals("PD011M1.DBF", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("請指定 PD011M1.dbf 檔案。", nameof(path));
        // FileShare.ReadWrite|Delete is deliberately non-exclusive and FileAccess.Read
        // prevents creation/updates to DBF, CDX and FPT.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[32];
        stream.ReadExactly(header);
        if (header[0] is not (0x03 or 0x30 or 0x31 or 0x32 or 0x83 or 0xF5))
            throw new InvalidDataException("不是支援的 FoxPro DBF 資料格式。");
        uint reportedCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));
        if (headerLength < 65 || headerLength > 65000 || recordLength < 2 || recordLength > 65535)
            throw new InvalidDataException("DBF 標頭長度或紀錄長度異常。");
        if (stream.Length < headerLength)
            throw new InvalidDataException("DBF 檔案標頭不完整。");

        var fields = new Dictionary<string, DbfField>(StringComparer.OrdinalIgnoreCase);
        int fallbackPosition = 1;
        for (int position = 32; position + 32 <= headerLength; position += 32)
        {
            stream.Position = position;
            int first = stream.ReadByte();
            if (first == 0x0D) break;
            if (first < 0) throw new EndOfStreamException("DBF 欄位描述中斷。");
            var descriptor = new byte[32];
            descriptor[0] = (byte)first;
            stream.ReadExactly(descriptor.AsSpan(1));
            string fieldName = Encoding.ASCII.GetString(descriptor, 0, 11).Split('\0')[0].Trim();
            char type = (char)descriptor[11];
            int length = descriptor[16];
            int displacement = (int)BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(12, 4));
            if (displacement == 0) displacement = fallbackPosition;
            if (length > 0 && displacement >= 1 && displacement + length <= recordLength)
                fields[fieldName] = new DbfField(fieldName, type, displacement, length);
            fallbackPosition += length;
        }

        DbfField Required(string name)
        {
            if (!fields.TryGetValue(name, out var field) || field.Type is not ('C' or 'N'))
                throw new InvalidDataException($"PD011M1 缺少可讀取的 {name} 文字欄位。");
            return field;
        }
        var num = Required("NUM");
        var id = Required("ID");
        var name = Required("NAME");

        var recordsAvailable = (stream.Length - headerLength) / recordLength;
        // Ignore a partially appended last record. When DBF is being written,
        // never read beyond the physical bytes observed when opened.
        long count = Math.Min((long)reportedCount, recordsAvailable);
        if (count > 5_000_000) throw new InvalidDataException("DBF 記錄數超出安全上限。");

        var numbers = new Dictionary<string, Patient>(StringComparer.Ordinal);
        var ids = new Dictionary<string, Patient>(StringComparer.OrdinalIgnoreCase);
        var duplicatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var row = new byte[recordLength];
        stream.Position = headerLength;

        string ReadField(DbfField field) =>
            Chinese.GetString(row, field.Offset, field.Length)
                .Trim('\0', ' ', '\u3000', '\r', '\n');

        for (long i = 0; i < count; i++)
        {
            try { stream.ReadExactly(row); }
            catch (EndOfStreamException)
            {
                throw new IOException("HIS 正在更新 DBF，讀取時資料長度改變；請稍後重試。");
            }
            if (row[0] != 0x20) continue; // 0x2A = deleted. Never resurrect it.
            string raw = ReadField(num);
            if (!CaptureIdentity.IsValid(raw)) continue;
            string normalized = CaptureIdentity.Require(raw);
            var patient = new Patient(normalized, ReadField(name), ReadField(id));
            if (!numbers.TryAdd(normalized, patient))
                throw new InvalidDataException("PD011M1 有重複的有效 NUM，無法安全確認患者。");
            if (patient.Id.Length > 0)
            {
                string key = patient.Id.ToUpperInvariant();
                if (!ids.TryAdd(key, patient))
                    duplicatedIds.Add(key);
            }
        }
        return new Snapshot(numbers, ids, duplicatedIds);
    }
}
