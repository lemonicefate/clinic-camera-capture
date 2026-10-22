namespace ClinicCameraCapture;

/// <summary>
/// Keeps an in-memory, read-only lookup index current with the DBF file.
/// Never persists patient names, identifiers, or raw records to disk.
/// </summary>
public sealed class PatientLookupService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PatientDbfReader.Snapshot? _snapshot;
    private string? _path;
    private long _length;
    private DateTime _modifiedUtc;

    public async Task<PatientDbfReader.Patient?> ByNumberAsync(string path, string number)
    {
        var index = await GetIndexAsync(path);
        return index.FindByNumber(number);
    }

    public async Task<PatientDbfReader.Patient?> ByIdAsync(string path, string id)
    {
        var index = await GetIndexAsync(path);
        return index.FindById(id);
    }

    public void Invalidate()
    {
        _snapshot = null;
        _path = null;
    }

    private async Task<PatientDbfReader.Snapshot> GetIndexAsync(string path)
    {
        await _gate.WaitAsync();
        try
        {
            string canonical = Path.GetFullPath(path);
            var info = new FileInfo(canonical);
            info.Refresh();
            if (!info.Exists) throw new FileNotFoundException("找不到指定的 PD011M1.dbf。");
            if (_snapshot is not null && string.Equals(_path, canonical, StringComparison.OrdinalIgnoreCase)
                && _length == info.Length && _modifiedUtc == info.LastWriteTimeUtc)
                return _snapshot;

            // DBF may be changing as the HIS registers a patient. Rebuild a
            // coherent snapshot and retry once if length/timestamp changes.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var beforeLength = info.Length;
                var beforeModified = info.LastWriteTimeUtc;
                var index = await Task.Run(() => PatientDbfReader.Load(canonical));
                info.Refresh();
                if (beforeLength == info.Length && beforeModified == info.LastWriteTimeUtc)
                {
                    _path = canonical;
                    _length = info.Length;
                    _modifiedUtc = info.LastWriteTimeUtc;
                    _snapshot = index;
                    return index;
                }
            }
            throw new IOException("PD011M1 在讀取時持續變更，請稍後再試。");
        }
        finally { _gate.Release(); }
    }
}
