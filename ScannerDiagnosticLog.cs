using System;
using System.IO;
using System.Text;

namespace LayoutFixer;

internal static class ScannerDiagnosticLog
{
    public static string LogPath => AppRuntime.GetDataPath("scanner_diagnostic.log");
    public static void Write(string message)
    {
        try { Directory.CreateDirectory(AppRuntime.DataDirectory); File.AppendAllText(LogPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8); } catch { }
    }
}

