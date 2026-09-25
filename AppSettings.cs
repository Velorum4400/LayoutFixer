using System.IO;
using System.Text.Json;

namespace LayoutFixer;

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; } = 25;
    public bool StartWithWindows { get; set; } = true;
    public bool FullTextEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
    public string FullTextHotkey { get; set; } = "Ctrl+Shift";

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
            settings.SettingsSchemaVersion = 25;
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
