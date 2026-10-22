using System.Text.Json;

namespace ClinicCameraCapture;

/// <summary>Persistent preferences only. Never persist the patient reference.</summary>
public sealed class CameraPreferences
{
    public string? CameraId { get; set; }
    public string Format { get; set; } = "png";
    public string Shortcut { get; set; } = "F9";
    public string Mode { get; set; } = "photo";
    public bool FullScreen { get; set; } = true;
    public string OutputFolder { get; set; } = CaptureStore.Folder;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClinicCameraCapture-MVP", "settings.json");

    public static CameraPreferences Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new CameraPreferences();
            var saved = JsonSerializer.Deserialize<CameraPreferences>(File.ReadAllText(ConfigPath))
                        ?? new CameraPreferences();
            if (saved.Format is not ("png" or "bmp")) saved.Format = "png";
            if (saved.Shortcut is not ("F8" or "F9" or "F10" or "F12")) saved.Shortcut = "F9";
            if (saved.Mode is not ("photo" or "video")) saved.Mode = "photo";
            try { saved.OutputFolder = OutputFolderPolicy.Normalize(saved.OutputFolder); }
            catch (ArgumentException) { saved.OutputFolder = CaptureStore.Folder; }
            return saved;
        }
        catch (Exception) { return new CameraPreferences(); }
    }

    public void Save()
    {
        OutputFolder = OutputFolderPolicy.Normalize(OutputFolder);
        var target = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>
/// Test-only local folder boundary; no direct writing into WM003 or network shares.
/// A production deployment requires a separate clinical-data threat model.
/// </summary>
public static class OutputFolderPolicy
{
    public static string Normalize(string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed) ||
            !Path.IsPathFullyQualified(proposed) ||
            proposed.StartsWith(@"\\") || proposed.StartsWith(@"//"))
            throw new ArgumentException("請選擇本機磁碟上的完整資料夾路徑（MVP 不支援網路共享）。");

        string path;
        try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(proposed)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ArgumentException("無效的儲存路徑。", ex); }

        if (Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar) == path.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("不允許直接使用磁碟根目錄。");

        var forbidden = Path.TrimEndingDirectorySeparator(Path.GetFullPath(@"D:\WM003"));
        if (path.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(forbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("不可將影像存入正式 WM003 資料夾。");

        // Junctions and symlinks may redirect supposedly local storage into HIS.
        var cursor = new DirectoryInfo(path);
        while (cursor is not null)
        {
            if (cursor.Exists && cursor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("MVP 不允許儲存路徑包含 junction 或 symbolic link。");
            cursor = cursor.Parent;
        }
        return path;
    }
}
