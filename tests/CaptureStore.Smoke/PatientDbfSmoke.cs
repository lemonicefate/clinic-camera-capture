using System.Buffers.Binary;
using System.Text;
using ClinicCameraCapture;

internal static class PatientDbfSmoke
{
    public static async Task RunAsync(string root)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp950 = Encoding.GetEncoding(950);
        string dir = Path.Combine(root, "synthetic-dbf");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "PD011M1.dbf");
        const int length = 1 + 6 + 12 + 24;
        const int headerLength = 32 + 3 * 32 + 1 + 263;
        byte[] header = new byte[headerLength];
        header[0] = 0x30; // VFP table; synthetic data only.
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), length);
        Field("NUM", 6, 1, 0);
        Field("ID", 12, 7, 1);
        Field("NAME", 24, 19, 2);
        header[32 + 3 * 32] = 0x0D;

        void Field(string name, int width, int displacement, int index)
        {
            int offset = 32 + index * 32;
            Encoding.ASCII.GetBytes(name).CopyTo(header, offset);
            header[offset + 11] = (byte)'C';
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset + 12, 4), (uint)displacement);
            header[offset + 16] = (byte)width;
        }

        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            output.Write(header);
            WriteRow("000090", "TESTID0001", "測試甲", false);
            WriteRow("000091", "", "測試乙", false);
            WriteRow("000092", "TESTID0002", "應被略過", true);
            WriteRow("000093", "TESTID0003", "測試丙", false);
            void WriteRow(string number, string id, string name, bool deleted)
            {
                var row = Enumerable.Repeat((byte)0x20, length).ToArray();
                row[0] = deleted ? (byte)0x2A : (byte)0x20;
                Put(number, 1, 6);
                Put(id, 7, 12);
                Put(name, 19, 24);
                output.Write(row);
                void Put(string text, int pos, int size)
                {
                    var encoded = cp950.GetBytes(text);
                    if (encoded.Length > size) throw new Exception("Fixture field too long");
                    encoded.CopyTo(row.AsSpan(pos, size));
                }
            }
        }

        // Simulate a separate HIS process keeping the table open; the reader
        // must not request exclusive locks and may only use FileAccess.Read.
        using (var activeHis = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            var lookup = PatientDbfReader.Load(path);
            Check(lookup.FindByNumber("90")?.Name == "測試甲", "CP950 NAME and padded NUM lookup");
            Check(lookup.FindByNumber("91")?.DisplayId == "0000000091", "empty ID fallback");
            Check(lookup.FindById("testid0001")?.Number == "000090", "ID index case insensitive");
            Check(lookup.FindById("TESTID0002") is null, "deleted row excluded");
            Check(lookup.FindByNumber("92") is null, "deleted NUM excluded");
            Check(lookup.FindById("0000000091") is null, "synthetic fallback not mistaken for real ID");
            var service = new PatientLookupService();
            Check((await service.ByNumberAsync(path, "90"))?.Name == "測試甲", "async cached index");
            Check((await service.ByIdAsync(path, "TESTID0003"))?.Number == "000093", "async reverse lookup");
        }
        Check(!File.Exists(Path.ChangeExtension(path, ".cdx")), "never creates CDX");
        Check(PatientDbfPathPolicy.Normalize(path) == Path.GetFullPath(path), "valid DBF path");
        bool rejected = false;
        try { PatientDbfPathPolicy.Normalize(Path.Combine(dir, "Other.dbf")); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "only selected PD011M1 name accepted");
        Console.WriteLine("PASS: synthetic FoxPro DBF CP950, NUM and ID lookup, deleted row, missing ID, read-only sharing.");
    }

    private static void Check(bool okay, string message)
    {
        if (!okay) throw new Exception("DBF assertion failed: " + message);
    }
}
