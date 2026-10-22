namespace ClinicCameraCapture;

/// <summary>
/// A local, synthetic reference only; no HIS lookup. Photo names are six ASCII
/// digits plus a two-digit sequence. Caller should only use fictitious values.
/// </summary>
public static class CaptureIdentity
{
    public static bool IsValid(string? value) =>
        value is { Length: >= 1 and <= 6 } &&
        value.All(c => c is >= '0' and <= '9');

    public static string Require(string? value) => IsValid(value)
        ? value!.PadLeft(6, '0')
        : throw new ArgumentException("病歷號必須為 1 至 6 位數字，儲存時補滿六位。");

    public static string PhotoName(string patientNumber, int sequence, string extension)
    {
        var normalized = Require(patientNumber);
        if (sequence is < 1 or > 99)
            throw new ArgumentOutOfRangeException(nameof(sequence), "每個病歷號只允許 01 到 99。");
        if (extension is not ("png" or "bmp"))
            throw new ArgumentException("照片格式僅支援 PNG 或 BMP。", nameof(extension));
        return $"{normalized}_{sequence:00}.{extension}";
    }

    public static string VideoName(string patientNumber) =>
        Require(patientNumber) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
        + "_" + Guid.NewGuid().ToString("N")[..8] + ".mp4";
}
