using System;
using System.IO;
using System.Text.Json;

namespace LayoutFixer;

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; } = 24;
    public bool StartWithWindows { get; set; } = true;
    public bool FullTextEnabled { get; set; } = true;
    public bool LastWordEnabled { get; set; } = true;
    public bool KeepSelectionAfterCorrection { get; set; } = false;

    // UI language: en, ru, he. English is the default.
    public string Language { get; set; } = "en";

    // Hotkeys may contain 1 to 3 keyboard keys.
    public string FullTextHotkey { get; set; } = "Ctrl+Shift";
    public string LastWordHotkey { get; set; } = "Insert";

    // Barcode scanner configuration. The scanner is expected to operate as a
    // USB HID keyboard. DevicePath is preferred; VID/PID is a reconnect fallback.
    public bool ScannerEnabled { get; set; } = false;
    public string ScannerDevicePath { get; set; } = "";
    public string ScannerVendorId { get; set; } = "";
    public string ScannerProductId { get; set; } = "";
    public string ScannerDisplayName { get; set; } = "";
    public int ScannerMinimumLength { get; set; } = 3;

    private static string SettingsPath => AppRuntime.GetDataPath("settings.json");

    public static AppSettings CreateDefault() => new();

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath)) ?? new AppSettings();

            // v0.16 changed the old default Ctrl+Alt to Insert.
            if (settings.SettingsSchemaVersion < 16 &&
                string.Equals(
                    settings.LastWordHotkey,
                    "Ctrl+Alt",
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.LastWordHotkey = "Insert";
            }

            if (settings.Language is not ("en" or "ru" or "he"))
                settings.Language = "en";

            if (settings.ScannerMinimumLength < 1 || settings.ScannerMinimumLength > 64)
                settings.ScannerMinimumLength = 3;

            settings.SettingsSchemaVersion = 24;
            settings.Save();

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void ResetToDefaults()
    {
        AppSettings defaults = CreateDefault();

        StartWithWindows = defaults.StartWithWindows;
        FullTextEnabled = defaults.FullTextEnabled;
        LastWordEnabled = defaults.LastWordEnabled;
        KeepSelectionAfterCorrection = defaults.KeepSelectionAfterCorrection;
        Language = defaults.Language;
        FullTextHotkey = defaults.FullTextHotkey;
        LastWordHotkey = defaults.LastWordHotkey;
        ScannerEnabled = defaults.ScannerEnabled;
        ScannerDevicePath = defaults.ScannerDevicePath;
        ScannerVendorId = defaults.ScannerVendorId;
        ScannerProductId = defaults.ScannerProductId;
        ScannerDisplayName = defaults.ScannerDisplayName;
        ScannerMinimumLength = defaults.ScannerMinimumLength;
        SettingsSchemaVersion = 24;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
        catch { }
    }
}
