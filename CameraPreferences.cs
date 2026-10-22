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
/// Normalizes a user-selected destination without imposing an application-level
/// location allowlist or denylist. This never reads or writes the target path.
/// Actual access is governed by Windows permissions and the caller's write probe.
/// </summary>
public static class OutputFolderPolicy
{
    public static string Normalize(string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed))
            throw new ArgumentException("請輸入要儲存相片與影片的資料夾路徑。");

        try
        {
            // Accept local, mapped, UNC, root, junction and symbolic-link paths;
            // a relative path is resolved to a full path before being saved.
            var expanded = Environment.ExpandEnvironmentVariables(proposed.Trim().Trim('"'));
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("儲存路徑格式無效。", ex);
        }
    }
}
