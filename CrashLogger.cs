using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LayoutFixer;

internal static class CrashLogger
{
    private static readonly object Sync = new();

    public static string LogPath => AppRuntime.GetDataPath("crash.log");

    public static void Initialize()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
        {
            WriteException("Application.ThreadException", e.Exception);

            try
            {
                MessageBox.Show(
                    $"LayoutFixer encountered an unexpected error.\r\n\r\nDetails were written to:\r\n{LogPath}",
                    AppInfo.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                WriteException("AppDomain.UnhandledException", ex, e.IsTerminating);
            else
                Write($"AppDomain.UnhandledException: {e.ExceptionObject}; terminating={e.IsTerminating}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        Write($"Application started. Version={AppInfo.Version}; OS={Environment.OSVersion}; .NET={Environment.Version}; UI thread={Environment.CurrentManagedThreadId}");
    }

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                string? directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging itself must never crash the application.
        }
    }

    public static void WriteException(string source, Exception exception, bool? terminating = null)
    {
        var sb = new StringBuilder();
        sb.Append(source);
        if (terminating.HasValue)
            sb.Append($"; terminating={terminating.Value}");
        sb.AppendLine();
        sb.AppendLine(exception.ToString());

        Write(sb.ToString().TrimEnd());
    }
}
