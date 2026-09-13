using System;
using System.Windows.Forms;

namespace LayoutFixer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        CrashLogger.Initialize();

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception ex)
        {
            CrashLogger.WriteException("Program.Main", ex, true);
            throw;
        }
    }
}
