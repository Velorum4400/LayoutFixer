using System;
using System.Threading;

namespace LayoutFixer;

// Clipboard needs STA. Keep the UI/hook thread pumping while another process
// handles SendInput and UIA; otherwise keyboard hooks can stall that process.
internal static class CorrectionWorker
{
    private static int _busy;
    public static bool IsBusy => Volatile.Read(ref _busy) != 0;

    public static bool TryRun(Action action)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        try
        {
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { CrashLogger.WriteException("Correction worker", ex); }
                finally { Volatile.Write(ref _busy, 0); }
            }) { IsBackground = true, Name = "LayoutFixer correction" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return true;
        }
        catch
        {
            Volatile.Write(ref _busy, 0);
            throw;
        }
    }
}
