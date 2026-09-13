using Microsoft.Win32;
using System.Diagnostics;

namespace LayoutFixer;

public static class StartupManager
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LayoutFixer";

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey? key =
            Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

        if (key == null)
            return;

        if (enabled)
        {
            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            key.SetValue(AppName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
