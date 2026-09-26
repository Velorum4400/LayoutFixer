using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LayoutFixer;

internal static class TextReplacementService
{
    public const int PasteRestoreDelayMilliseconds = 100;
    private static int _running;

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static bool TryReplaceAllText(IntPtr targetWindow)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            Log("SKIP: another full-text replacement is already running");
            return false;
        }

        var total = Stopwatch.StartNew();
        ClipboardSnapshot? snapshot = null;
        bool ownsClipboard = false;
        bool restorationHandled = false;
        uint ownedSequence = 0;
        bool pasted = false;
        Log($"========== START mode=fullText, target=0x{targetWindow.ToInt64():X}");

        try
        {
            if (targetWindow == IntPtr.Zero || GetForegroundWindow() != targetWindow)
                return Fail("active window changed before operation started", targetWindow);

            if (!KeyboardLayoutService.TryGetCurrentAndNext(targetWindow,
                out KeyboardLayoutInfo sourceLayout, out KeyboardLayoutInfo targetLayout))
            {
                Log("FAIL: source or target layout could not be determined");
                return false;
            }
            Log($"Layouts: source={sourceLayout.DisplayName} (0x{sourceLayout.Handle.ToInt64():X}), target={targetLayout.DisplayName} (0x{targetLayout.Handle.ToInt64():X})");
            if (!KeyboardLayoutService.TryGetMap(sourceLayout, out KeyboardLayoutMap sourceMap) ||
                !KeyboardLayoutService.TryGetMap(targetLayout, out KeyboardLayoutMap targetMap))
            {
                Log("FAIL: source or target keyboard layout map is unavailable");
                return false;
            }

            var clipboardTimer = Stopwatch.StartNew();
            if (!ClipboardService.TryCapture(out snapshot))
            {
                Log($"FAIL: Clipboard snapshot timeout after {clipboardTimer.ElapsedMilliseconds} ms");
                return false;
            }
            Log($"Clipboard snapshot captured: formats={snapshot.FormatCount}, elapsed={clipboardTimer.ElapsedMilliseconds} ms");

            if (!KeyboardInputService.SelectAll())
            {
                Log("FAIL: Ctrl+A SendInput failed");
                return false;
            }
            Log("Ctrl+A sent");
            Thread.Sleep(30);
            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed before Ctrl+C", targetWindow);

            uint sequenceBeforeCopy = ClipboardService.SequenceNumber;
            var copyDispatchTimer = Stopwatch.StartNew();
            if (!KeyboardInputService.Copy())
            {
                Log("FAIL: Ctrl+C SendInput failed");
                return false;
            }
            Log("Ctrl+C sent");

            bool copyCompleted = ClipboardService.WaitForTextChange(sequenceBeforeCopy,
                out string sourceText, out ownedSequence, out long copyWait);
            Log($"Clipboard copy wait: elapsed={copyWait} ms, changed={copyCompleted}");
            if (!copyCompleted)
            {
                Log("FAIL: Clipboard copy timeout");
                return false;
            }
            ownsClipboard = true;
            Log($"Copy result: success=True, textLength={sourceText.Length}");
            ClipboardDiagnostics.ObserveAfterCopy(copyDispatchTimer, sequenceBeforeCopy,
                ownedSequence, copyWait);

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed after text acquisition", targetWindow);
            if (sourceText.Length == 0)
            {
                Log("CANCEL: active text field is empty or Clipboard contains no text");
                return false;
            }

            string convertedText = LayoutConverter.Convert(sourceText, sourceMap, targetMap, out int unchangedCount);
            Log($"Conversion result: success=True, sourceLength={sourceText.Length}, convertedLength={convertedText.Length}, unchanged={unchangedCount}");

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed before Clipboard write", targetWindow);
            if (!ClipboardService.IsCurrentSequence(ownedSequence))
            {
                Log("CANCEL: Clipboard was changed by another process before converted text was written");
                ownsClipboard = false;
                return false;
            }

            clipboardTimer.Restart();
            if (!ClipboardService.TrySetText(convertedText, out ownedSequence))
            {
                Log($"FAIL: converted text Clipboard write timeout after {clipboardTimer.ElapsedMilliseconds} ms");
                return false;
            }
            Log($"Converted text written to Clipboard: elapsed={clipboardTimer.ElapsedMilliseconds} ms, sequence={ownedSequence}");

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed immediately before Ctrl+V", targetWindow);
            if (!KeyboardInputService.Paste())
            {
                Log("FAIL: Ctrl+V SendInput failed");
                return false;
            }
            pasted = true;
            Log("Ctrl+V sent: success=True");

            Thread.Sleep(PasteRestoreDelayMilliseconds);
            ClipboardRestoreResult restoreResult = ClipboardService.RestoreIfUnchanged(snapshot, ownedSequence);
            restorationHandled = true;
            ownsClipboard = false;
            LogRestoreResult(restoreResult);

            bool layoutSwitched = KeyboardLayoutService.SwitchLayout(targetWindow, targetLayout);
            Log($"Layout switch: success={layoutSwitched}, target={targetLayout.DisplayName}");
            if (!layoutSwitched)
                return false;

            Log("SUCCESS");
            return true;
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (!restorationHandled && ownsClipboard && snapshot != null)
                LogRestoreResult(ClipboardService.RestoreIfUnchanged(snapshot, ownedSequence));
            Log($"Operation result: pasted={pasted}, elapsed={total.ElapsedMilliseconds} ms");
            Log("========== END ==========" + Environment.NewLine);
            Volatile.Write(ref _running, 0);
        }
    }

    private static bool Fail(string reason, IntPtr targetWindow)
    {
        Log($"CANCEL: {reason}; target=0x{targetWindow.ToInt64():X}, current=0x{GetForegroundWindow().ToInt64():X}");
        return false;
    }

    private static void LogRestoreResult(ClipboardRestoreResult result)
    {
        switch (result)
        {
            case ClipboardRestoreResult.Restored:
                Log("Clipboard restore: success=True");
                break;
            case ClipboardRestoreResult.SkippedBecauseChanged:
                Log("Clipboard restore: skipped because Clipboard was changed by another process");
                break;
            default:
                Log("Clipboard restore: failed after timeout");
                break;
        }
    }

    private static void Log(string message) => DiagnosticLogStore.Write(message);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

