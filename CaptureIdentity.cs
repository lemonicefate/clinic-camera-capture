using System.Text.RegularExpressions;

namespace ClinicCameraCapture;

/// <summary>Clinical reference input remains local; this MVP never reads or updates the HIS.</summary>
public static class CaptureIdentity
{
    public static bool IsValid(string? value) =>
        value is { Length: 6 } && value.All(c => c is >= '0' and <= '9');

    public static string Require(string? value) => IsValid(value)
        ? value!
        : throw new ArgumentException("請輸入六位數病歷號（保留前導零）。");

    public static string Filename(string patientNumber, string extension)
    {
        Require(patientNumber);
        if (extension is not ("png" or "bmp" or "mp4"))
            throw new ArgumentException("不支援的檔案類型。", nameof(extension));
        return patientNumber + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
             + "_" + Guid.NewGuid().ToString("N")[..8] + "." + extension;
    }
}
