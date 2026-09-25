using System;
using System.IO;
using System.Text;

namespace LayoutFixer;

internal static class DiagnosticLogStore
{
    private static readonly object Sync = new();
    public static string Path => AppRuntime.GetDataPath("diagnostic.log");
    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppRuntime.DataDirectory);
                File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n", Encoding.UTF8);
            }
        }
        catch { }
    }
    public static void Clear()
    {
        lock (Sync)
        {
            Directory.CreateDirectory(AppRuntime.DataDirectory);
            File.WriteAllText(Path, string.Empty);
        }
    }
    public static string Read()
    {
        lock (Sync)
        {
            if (!File.Exists(Path)) return string.Empty;
            using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool truncated = file.Length > 512 * 1024;
            if (truncated) file.Seek(-512 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(file, Encoding.UTF8);
            if (truncated) reader.ReadLine(); // Skip partial UTF-8/line at the boundary.
            return (truncated ? UiText.Get("log_tail") + "\r\n" : "") + reader.ReadToEnd();
        }
    }
}
