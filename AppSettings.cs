using System;
using System.IO;
using System.Text.Json;

namespace MonitorMirror;

internal sealed class AppSettings
{
    public string? SourceDeviceName { get; set; }
    public string? DestDeviceName { get; set; }
    public int ScalePercent { get; set; } = 10;
    public WarpMode Warp { get; set; } = WarpMode.Fill;
    public bool HotkeyEnabled { get; set; } = true;
    public bool HotkeyHold { get; set; } = true;
    public TriggerType TriggerType { get; set; } = TriggerType.None;
    public int HotkeyKey { get; set; }
    public int HotkeyMouseButton { get; set; }
    public uint HotkeyModifiers { get; set; }
    public bool DarkTheme { get; set; } = true;
}

internal static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitorMirror", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // corrupt or unreadable settings file; fall back to defaults
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // best-effort persistence; ignore failures (e.g. locked file, no permissions)
        }
    }
}
