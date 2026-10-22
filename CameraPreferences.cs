using System.Text.Json;

namespace ClinicCameraCapture;

/// <summary>
/// Only camera UI preferences are persisted, never patient identifiers or images.
/// Stored in the isolated MVP directory, not in D:\WM003.
/// </summary>
public sealed class CameraPreferences
{
    public string? CameraId { get; set; }
    public string Format { get; set; } = "png";
    public string Shortcut { get; set; } = "F9";
    public string Mode { get; set; } = "photo";
    public bool FullScreen { get; set; } = true;

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
            return saved;
        }
        catch (Exception)
        {
            // Malformed preferences must never block the camera or patient image capture.
            return new CameraPreferences();
        }
    }

    public void Save()
    {
        var target = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
