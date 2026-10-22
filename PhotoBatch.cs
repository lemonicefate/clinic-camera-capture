namespace ClinicCameraCapture;

/// <summary>
/// Pictures captured during this app run, grouped by normalized reference.
/// Never scans historical photos from disk or mixes different patients.
/// </summary>
public sealed class PhotoBatch
{
    private readonly Dictionary<string, List<string>> _photos = new(StringComparer.Ordinal);

    public void Add(string patientNumber, string path)
    {
        var id = CaptureIdentity.Require(patientNumber);
        if (!_photos.TryGetValue(id, out var files))
            _photos[id] = files = new List<string>();
        files.Add(path);
    }

    public IReadOnlyList<string> For(string? patientNumber) =>
        CaptureIdentity.IsValid(patientNumber) &&
        _photos.TryGetValue(CaptureIdentity.Require(patientNumber), out var paths)
            ? paths.AsReadOnly()
            : Array.Empty<string>();
}
