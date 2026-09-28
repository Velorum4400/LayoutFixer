using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LayoutFixer;

internal static class TextReplacementService
{
    public static int PasteRestoreDelayMilliseconds { get; set; } = 100;
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
        var operation = new TextReplacementOperation { TargetWindow = targetWindow };
        bool clipboardContainsOurData = false;
        bool pasted = false;
        Log($"========== START operation={operation.Id:N}, mode=fullText, target=0x{targetWindow.ToInt64():X}");

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
            operation.SourceLayout = sourceLayout;
            operation.TargetLayout = targetLayout;
            Log($"Layouts: source={sourceLayout.DisplayName} (0x{sourceLayout.Handle.ToInt64():X}), target={targetLayout.DisplayName} (0x{targetLayout.Handle.ToInt64():X})");
            if (!KeyboardLayoutService.TryGetMap(sourceLayout, out KeyboardLayoutMap sourceMap) ||
                !KeyboardLayoutService.TryGetMap(targetLayout, out KeyboardLayoutMap targetMap))
            {
                Log("FAIL: source or target keyboard layout map is unavailable");
                return false;
            }

            var clipboardTimer = Stopwatch.StartNew();
            if (!ClipboardService.TryCaptureStable(out ClipboardSnapshot initialSnapshot,
                    out uint initialSnapshotSequence, out long snapshotElapsed))
            {
                Log($"FAIL: Clipboard snapshot timeout after {clipboardTimer.ElapsedMilliseconds} ms");
                return false;
            }
            operation.RestoreSnapshot = initialSnapshot;
            operation.RestoreSnapshotSequence = initialSnapshotSequence;
            Log($"Clipboard snapshot captured: formats={initialSnapshot.FormatCount}, sequence={initialSnapshotSequence}, elapsed={snapshotElapsed} ms");

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

            if (!ClipboardService.WaitForStableCopy(sequenceBeforeCopy, copyDispatchTimer,
                    out ClipboardCopyResult copyResult))
            {
                Log($"FAIL: Clipboard copy did not stabilize within {copyResult.ElapsedMilliseconds} ms");
                return false;
            }
            operation.SourceText = copyResult.Text;
            operation.CopyTextHash = copyResult.TextHash;
            operation.CopyTextLength = copyResult.Text.Length;
            operation.LastObservedClipboardSequence = copyResult.LastSequence;
            clipboardContainsOurData = copyResult.ClipboardContainsSourceText;
            if (copyResult.NewExternalSnapshot != null)
            {
                operation.RestoreSnapshot = copyResult.NewExternalSnapshot;
                operation.RestoreSnapshotSequence = copyResult.NewExternalSnapshotSequence;
            }
            Log($"Clipboard copy stabilized: elapsed={copyResult.ElapsedMilliseconds} ms, changes={copyResult.ChangeCount}, sequence={copyResult.LastSequence}, textLength={operation.CopyTextLength}, textHash={operation.CopyTextHash}");

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed after text acquisition", targetWindow);
            if (operation.SourceText.Length == 0)
            {
                Log("CANCEL: active text field is empty or Clipboard contains no text");
                return false;
            }

            if (clipboardContainsOurData)
            {
                ClipboardRestoreResult earlyRestore = ClipboardService.RestoreIfUnchanged(
                    operation.RestoreSnapshot, operation.LastObservedClipboardSequence,
                    out uint restoredSequence);
                LogRestoreResult(earlyRestore, "after Copy");
                if (earlyRestore == ClipboardRestoreResult.Restored)
                {
                    operation.RestoreSnapshotSequence = restoredSequence;
                    clipboardContainsOurData = false;
                }
                else if (earlyRestore == ClipboardRestoreResult.SkippedBecauseChanged)
                {
                    if (!TryAdoptCurrentClipboard(operation, "during early restore"))
                        return false;
                    clipboardContainsOurData = false;
                }
                else
                    return false;
            }
            else
            {
                Log($"Clipboard early restore not required: current sequence={operation.RestoreSnapshotSequence} already represents the latest external snapshot");
            }

            operation.ConvertedText = LayoutConverter.Convert(operation.SourceText, sourceMap,
                targetMap, out int unchangedCount);
            Log($"Conversion result: success=True, sourceLength={operation.SourceText.Length}, convertedLength={operation.ConvertedText.Length}, unchanged={unchangedCount}");

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed before Clipboard write", targetWindow);
            if (!ClipboardService.IsCurrentSequence(operation.RestoreSnapshotSequence) &&
                !TryAdoptCurrentClipboard(operation, "before Paste preparation"))
                return false;

            clipboardTimer.Restart();
            if (!ClipboardService.TrySetText(operation.ConvertedText,
                    out uint pasteSequence))
            {
                Log($"FAIL: converted text Clipboard write timeout after {clipboardTimer.ElapsedMilliseconds} ms");
                return false;
            }
            operation.OurPasteClipboardSequence = pasteSequence;
            clipboardContainsOurData = true;
            Log($"Converted text written to Clipboard: elapsed={clipboardTimer.ElapsedMilliseconds} ms, sequence={operation.OurPasteClipboardSequence}");

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed immediately before Ctrl+V", targetWindow);
            if (!KeyboardInputService.Paste())
            {
                Log("FAIL: Ctrl+V SendInput failed");
                return false;
            }
            pasted = true;
            Log("Ctrl+V sent: success=True");

            Thread.Sleep(Math.Max(0, PasteRestoreDelayMilliseconds));
            ClipboardRestoreResult restoreResult = ClipboardService.RestoreIfUnchanged(
                operation.RestoreSnapshot, operation.OurPasteClipboardSequence, out _);
            LogRestoreResult(restoreResult, "after Paste");
            clipboardContainsOurData = false;

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
            if (clipboardContainsOurData)
                LogRestoreResult(ClipboardService.RestoreIfUnchanged(operation.RestoreSnapshot,
                    operation.OurPasteClipboardSequence != 0
                        ? operation.OurPasteClipboardSequence
                        : operation.LastObservedClipboardSequence), "during cleanup");
            Log($"Operation result: operation={operation.Id:N}, pasted={pasted}, elapsed={total.ElapsedMilliseconds} ms");
            Log("========== END ==========" + Environment.NewLine);
            Volatile.Write(ref _running, 0);
        }
    }

    private static bool Fail(string reason, IntPtr targetWindow)
    {
        Log($"CANCEL: {reason}; target=0x{targetWindow.ToInt64():X}, current=0x{GetForegroundWindow().ToInt64():X}");
        return false;
    }

    private static bool TryAdoptCurrentClipboard(TextReplacementOperation operation, string stage)
    {
        if (!ClipboardService.TryCaptureStable(out ClipboardSnapshot snapshot,
                out uint sequence, out long elapsed))
        {
            Log($"CANCEL: Clipboard content changed unexpectedly {stage}; stable snapshot unavailable after {elapsed} ms");
            return false;
        }
        operation.RestoreSnapshot = snapshot;
        operation.RestoreSnapshotSequence = sequence;
        Log($"New external Clipboard snapshot adopted {stage}: formats={snapshot.FormatCount}, sequence={sequence}, elapsed={elapsed} ms");
        return true;
    }

    private static void LogRestoreResult(ClipboardRestoreResult result, string stage = "")
    {
        string suffix = string.IsNullOrEmpty(stage) ? string.Empty : $" {stage}";
        switch (result)
        {
            case ClipboardRestoreResult.Restored:
                Log($"Clipboard restore{suffix}: success=True");
                break;
            case ClipboardRestoreResult.SkippedBecauseChanged:
                Log($"Clipboard restore{suffix}: skipped because Clipboard content changed unexpectedly");
                break;
            default:
                Log($"Clipboard restore{suffix}: failed after timeout");
                break;
        }
    }

    private static void Log(string message) => DiagnosticLogStore.Write(message);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

