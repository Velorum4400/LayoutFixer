using System;
using System.Threading;

namespace LayoutFixer;

// Clipboard needs STA. Keep the UI/hook thread pumping while the worker handles
// Clipboard access and SendInput so the global hotkey remains responsive.
internal static class CorrectionWorker
{
    // Keep a stalled UI Automation provider from blocking later corrections.
    private const int WatchdogTimeoutMs = 3000;
    private static int _busy;
    private static int _nextOperation;
    private static int _activeOperation;
    [ThreadStatic] private static int _operationOnThisThread;
    public static bool IsBusy => Volatile.Read(ref _busy) != 0;
    public static bool CanContinue =>
        _operationOnThisThread != 0 &&
        _operationOnThisThread == Volatile.Read(ref _activeOperation);

    public static bool TryRun(Action action)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        int operation = Interlocked.Increment(ref _nextOperation);
        Volatile.Write(ref _activeOperation, operation);
        try
        {
            var thread = new Thread(() =>
            {
                using var watchdog = new System.Threading.Timer(_ => Expire(operation), null, WatchdogTimeoutMs, Timeout.Infinite);
                _operationOnThisThread = operation;
                try { action(); }
                catch (Exception ex) { CrashLogger.WriteException("Correction worker", ex); }
                finally
                {
                    _operationOnThisThread = 0;
                    if (Interlocked.CompareExchange(ref _activeOperation, 0, operation) == operation)
                        Volatile.Write(ref _busy, 0);
                }
            }) { IsBackground = true, Name = "LayoutFixer correction" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return true;
        }
        catch
        {
            Volatile.Write(ref _activeOperation, 0);
            Volatile.Write(ref _busy, 0);
            throw;
        }
    }

    private static void Expire(int operation)
    {
        if (Interlocked.CompareExchange(ref _activeOperation, 0, operation) != operation)
            return;
        Volatile.Write(ref _busy, 0);
        DiagnosticLogStore.Write(
            "FAIL: correction watchdog elapsed after 3000 ms; operation canceled and hotkeys re-enabled");
    }
}
