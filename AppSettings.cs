using System;
using System.IO;
using System.Text.Json;

namespace LayoutFixer;

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; } = 22;
    public bool StartWithWindows { get; set; } = true;
    public bool FullTextEnabled { get; set; } = true;
    public bool LastWordEnabled { get; set; } = true;

    // UI language: en, ru, he. English is the default.
    public string Language { get; set; } = "en";

    // Hotkeys may contain 1 to 3 keyboard keys.
    public string FullTextHotkey { get; set; } = "Ctrl+Shift";
    public string LastWordHotkey { get; set; } = "Insert";

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LayoutFixer",
            "settings.json");

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

            settings.SettingsSchemaVersion = 22;
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
        Language = defaults.Language;
        FullTextHotkey = defaults.FullTextHotkey;
        LastWordHotkey = defaults.LastWordHotkey;
        SettingsSchemaVersion = 22;
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
