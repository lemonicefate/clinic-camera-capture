namespace ClinicCameraCapture;

/// <summary>
/// Accept only the designated PD011M1 DBF for this read-only lookup.
/// Validates path shape without opening any production clinical records.
/// </summary>
public static class PatientDbfPathPolicy
{
    public static string Normalize(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            throw new ArgumentException("請選擇 PD011M1.dbf。");
        string path;
        try
        {
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("DBF 路徑無效。", ex);
        }
        if (!Path.GetFileName(path).Equals("PD011M1.dbf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("指定的檔名必須是 PD011M1.dbf。");
        return path;
    }
}
