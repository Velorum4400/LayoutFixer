using System;
using System.IO;
using System.Text;

namespace LayoutFixer;

internal static class ScannerDiagnosticLog
{
    private static readonly object Sync = new();

    public static string LogPath => AppRuntime.GetDataPath("scaner_diagnostic.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppRuntime.DataDirectory);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Scanner diagnostics must never affect the application itself.
        }
    }

    public static void WriteException(string source, Exception exception)
    {
        Write($"{source}{Environment.NewLine}{exception}");
    }
}
