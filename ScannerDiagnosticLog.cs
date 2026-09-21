using System;
using System.IO;
using System.Text;

namespace LayoutFixer;

internal static class ScannerDiagnosticLog
{
    public static string LogPath => DiagnosticLogStore.PathFor(true);

    public static void Write(string message)
    {
        DiagnosticLogStore.Write(true, message);
    }

    public static void WriteException(string source, Exception exception)
    {
        Write($"{source}{Environment.NewLine}{exception}");
    }
}
