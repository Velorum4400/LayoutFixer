using System;

namespace LayoutFixer;

internal static class ScannerDiagnosticLog
{
    public static void Write(string message)
    {
        DiagnosticLogStore.Write(true, message);
    }

    public static void WriteException(string source, Exception exception)
    {
        Write($"{source}{Environment.NewLine}{exception}");
    }
}
