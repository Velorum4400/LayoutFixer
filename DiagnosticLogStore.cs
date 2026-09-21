using System;
using System.IO;
using System.Text;

namespace LayoutFixer;

internal static class DiagnosticLogStore
{
    private static readonly object Sync = new();
    public static string PathFor(bool scanner)
    {
        string path = AppRuntime.GetDataPath(scanner ? "scanner_diagnostic.log" : "diagnostic.log");
        if (scanner)
        {
            lock (Sync)
            {
                string legacy = AppRuntime.GetDataPath("scaner_diagnostic.log");
                if (!File.Exists(path) && File.Exists(legacy)) File.Move(legacy, path);
            }
        }
        return path;
    }
    public static void Write(bool scanner, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppRuntime.DataDirectory);
                File.AppendAllText(PathFor(scanner), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n", Encoding.UTF8);
            }
        }
        catch { }
    }
    public static void Clear(bool scanner)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(AppRuntime.DataDirectory);
            File.WriteAllText(PathFor(scanner), string.Empty);
        }
    }
    public static string Read(bool scanner)
    {
        lock (Sync)
        {
            string path = PathFor(scanner);
            if (!File.Exists(path)) return string.Empty;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool truncated = file.Length > 512 * 1024;
            if (truncated) file.Seek(-512 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(file, Encoding.UTF8);
            if (truncated) reader.ReadLine(); // Skip partial UTF-8/line at the boundary.
            return (truncated ? UiText.Get("log_tail") + "\r\n" : "") + reader.ReadToEnd();
        }
    }
}
