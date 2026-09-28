using System.IO;
using System;
using System.Linq;
using System.Text.Json;

namespace LayoutFixer;

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; } = 26;
    public bool StartWithWindows { get; set; } = true;
    public bool FullTextEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
    public string FullTextHotkey { get; set; } = "Ctrl+Shift";
    public bool LastWordEnabled { get; set; } = true;
    public string LastWordHotkey { get; set; } = "Insert";
    public bool SelectedTextEnabled { get; set; } = true;
    public string SelectedTextHotkey { get; set; } = "Pause";
    public bool ScannerEnabled { get; set; }
    public string ScannerDevicePath { get; set; } = "";
    public string ScannerVendorId { get; set; } = "";
    public string ScannerProductId { get; set; } = "";
    public string ScannerDisplayName { get; set; } = "";

    private static string SettingsPath => AppRuntime.GetDataPath("settings.json");
    public static AppSettings CreateDefault() => new();

    public static AppSettings Load()
    {
        try
        {
            AppSettings settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                : new AppSettings();
            if (settings.Language is not ("en" or "ru" or "he"))
                settings.Language = "en";
            if (!HotkeyDefinition.IsValid(settings.FullTextHotkey))
                settings.FullTextHotkey = "Ctrl+Shift";
            if (!HotkeyDefinition.IsValid(settings.LastWordHotkey))
                settings.LastWordHotkey = "Insert";
            if (!HotkeyDefinition.IsValid(settings.SelectedTextHotkey))
                settings.SelectedTextHotkey = "Pause";
            if (new[] { settings.FullTextHotkey, settings.LastWordHotkey,
                    settings.SelectedTextHotkey }
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            {
                settings.FullTextHotkey = "Ctrl+Shift";
                settings.LastWordHotkey = "Insert";
                settings.SelectedTextHotkey = "Pause";
            }
            settings.SettingsSchemaVersion = 26;
            settings.Save();
            return settings;
        }
        catch { return new AppSettings(); }
    }

    public void ResetToDefaults()
    {
        AppSettings defaults = CreateDefault();
        StartWithWindows = defaults.StartWithWindows;
        FullTextEnabled = defaults.FullTextEnabled;
        Language = defaults.Language;
        FullTextHotkey = defaults.FullTextHotkey;
        LastWordEnabled = defaults.LastWordEnabled;
        LastWordHotkey = defaults.LastWordHotkey;
        SelectedTextEnabled = defaults.SelectedTextEnabled;
        SelectedTextHotkey = defaults.SelectedTextHotkey;
        ScannerEnabled = defaults.ScannerEnabled;
        ScannerDevicePath = defaults.ScannerDevicePath;
        ScannerVendorId = defaults.ScannerVendorId;
        ScannerProductId = defaults.ScannerProductId;
        ScannerDisplayName = defaults.ScannerDisplayName;
        SettingsSchemaVersion = defaults.SettingsSchemaVersion;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

