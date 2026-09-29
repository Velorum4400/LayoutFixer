using System;
using System.IO;
using System.Text;

namespace LayoutFixer;

internal static class HotkeyWatchdogLog
{
    private static readonly object Sync = new();
    private static readonly TimeSpan HealthyLogRetention = TimeSpan.FromMinutes(15);
    private static DateTime _intervalStartedUtc;
    private static bool _stopped;

    public static string LogPath => AppRuntime.GetDataPath("hotkey_watchdog.log");

    public static void StartSession()
    {
        lock (Sync)
        {
            _stopped = false;
            _intervalStartedUtc = DateTime.UtcNow;
            ResetFile("HOTKEY WATCHDOG START: monitoring active");
        }
    }

    public static void WriteHealthy(string message)
    {
        lock (Sync)
        {
            if (_stopped)
                return;

            if (DateTime.UtcNow - _intervalStartedUtc >= HealthyLogRetention)
            {
                _intervalStartedUtc = DateTime.UtcNow;
                ResetFile("HOTKEY WATCHDOG START: previous healthy interval cleared after 15 minutes");
            }

            Append(message);
        }
    }

    public static void Stop(string reason)
    {
        lock (Sync)
        {
            if (_stopped)
                return;

            _stopped = true;
            Append($"HOTKEY WATCHDOG STOPPED: {reason}");
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _stopped = false;
            _intervalStartedUtc = DateTime.UtcNow;
            ResetFile("HOTKEY WATCHDOG START: log cleared by user");
        }
    }

    public static string Read()
    {
        lock (Sync)
        {
            if (!File.Exists(LogPath))
                return string.Empty;
            using var file = new FileStream(LogPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    private static void ResetFile(string firstLine)
    {
        Directory.CreateDirectory(AppRuntime.DataDirectory);
        File.WriteAllText(LogPath, Format(firstLine), Encoding.UTF8);
    }

    private static void Append(string message)
    {
        Directory.CreateDirectory(AppRuntime.DataDirectory);
        File.AppendAllText(LogPath, Format(message), Encoding.UTF8);
    }

    private static string Format(string message) =>
        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
}

