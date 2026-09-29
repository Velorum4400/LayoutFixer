using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LayoutFixer;

internal static class TextReplacementService
{
    public static int PasteRestoreDelayMilliseconds { get; set; } = 100;
    private const int MaxLastWordSearchIterations = 10;
    private static int _running;

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static bool TryReplaceAllText(IntPtr targetWindow) =>
        TryReplaceText(targetWindow, TextReplacementOperationType.FullText);

    public static bool TryReplaceText(IntPtr targetWindow, TextReplacementOperationType operationType)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            Log("SKIP: another full-text replacement is already running");
            return false;
        }

        var total = Stopwatch.StartNew();
        var operation = new TextReplacementOperation
        {
            TargetWindow = targetWindow,
            Type = operationType
        };
        bool clipboardContainsOurData = false;
        bool pasted = false;
        Log($"========== START operation={operation.Id:N}, OperationType={operation.Type}, target=0x{targetWindow.ToInt64():X}");
        if (operation.Type == TextReplacementOperationType.LastWord)
            Log("LastWord PERF start t=0 ms");

        try
        {
            if (targetWindow == IntPtr.Zero || GetForegroundWindow() != targetWindow)
                return Fail("active window changed before operation started", targetWindow);

            if (operation.Type == TextReplacementOperationType.LastWord)
            {
                LayoutResolutionDiagnostic diagnostic = KeyboardLayoutService.GetResolutionDiagnostic(targetWindow);
                GuiInputContextDiagnostic gui = KeyboardLayoutService.GetGuiInputContextDiagnostic(diagnostic.TargetThreadId);
                Log("LAYOUT DIAGNOSTIC BEGIN");
                Log($"ForegroundWindow=0x{GetForegroundWindow().ToInt64():X}, TargetWindow=0x{diagnostic.TargetWindow.ToInt64():X}");
                Log($"ForegroundWindowProcessId={diagnostic.TargetProcessId}, ForegroundWindowThreadId={diagnostic.TargetThreadId}");
                Log($"CurrentProcessId={diagnostic.CurrentProcessId}, CurrentThreadId={diagnostic.CurrentThreadId}");
                Log($"GetKeyboardLayout(foregroundThreadId)=0x{diagnostic.TargetThreadLayout.ToInt64():X}");
                Log($"GetKeyboardLayout(currentThreadId)=0x{diagnostic.CurrentThreadLayout.ToInt64():X}");
                Log($"GetGUIThreadInfo success={gui.QuerySucceeded}, FocusWindow=0x{gui.FocusWindow.ToInt64():X}, FocusProcessId={gui.FocusProcessId}, FocusThreadId={gui.FocusThreadId}, GetKeyboardLayout(focusThreadId)=0x{gui.FocusThreadLayout.ToInt64():X}");
                Log($"CaretWindow=0x{gui.CaretWindow.ToInt64():X}, CaretProcessId={gui.CaretProcessId}, CaretThreadId={gui.CaretThreadId}, GetKeyboardLayout(caretThreadId)=0x{gui.CaretThreadLayout.ToInt64():X}");
                Log($"CachedLayouts=[{diagnostic.CachedLayouts}]");
            }

            if (!KeyboardLayoutService.TryGetCurrentAndNext(targetWindow,
                out KeyboardLayoutInfo sourceLayout, out KeyboardLayoutInfo targetLayout))
            {
                Log("FAIL: source or target layout could not be determined");
                return false;
            }
            operation.SourceLayout = sourceLayout;
            operation.TargetLayout = targetLayout;
            if (operation.Type == TextReplacementOperationType.LastWord)
                Log($"ResolvedSourceLayout={sourceLayout.DisplayName} (0x{sourceLayout.Handle.ToInt64():X}), ResolvedTargetLayout={targetLayout.DisplayName} (0x{targetLayout.Handle.ToInt64():X}){Environment.NewLine}LAYOUT DIAGNOSTIC END");
            Log($"Layouts: source={sourceLayout.DisplayName} (0x{sourceLayout.Handle.ToInt64():X}), target={targetLayout.DisplayName} (0x{targetLayout.Handle.ToInt64():X})");
            if (!KeyboardLayoutService.TryGetMap(sourceLayout, out KeyboardLayoutMap sourceMap) ||
                !KeyboardLayoutService.TryGetMap(targetLayout, out KeyboardLayoutMap targetMap))
            {
                Log("FAIL: source or target keyboard layout map is unavailable");
                return false;
            }
            operation.SourceMap = sourceMap;
            operation.TargetMap = targetMap;

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

            if (!PrepareSelection(operation, total))
            {
                Log($"FAIL: selection preparation failed for OperationType={operation.Type}");
                return false;
            }
            if (operation.Type == TextReplacementOperationType.LastWord)
            {
                if (!operation.LastWordDirectReplaceReady)
                {
                    Log("CANCEL: LastWord Direct Replace candidate was not prepared");
                    return false;
                }

                operation.SourceText = operation.LastWordSearchFragment;
                operation.ConvertedText = operation.LastWordCandidateReplacement;
                operation.CopyTextLength = operation.SourceText.Length;
                Log($"LastWord DIRECT-REPLACE paste: selectionLength={operation.LastWordSearchSelectionLength}, replacementLength={operation.ConvertedText.Length}");
            }
            else
            {
                if (operation.Type == TextReplacementOperationType.FullText)
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
            }

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed after text acquisition", targetWindow);
            if (operation.SourceText.Length == 0)
            {
                Log("CANCEL: active text field is empty or Clipboard contains no text");
                return false;
            }

            if (operation.Type != TextReplacementOperationType.LastWord && clipboardContainsOurData)
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
            else if (operation.Type != TextReplacementOperationType.LastWord)
            {
                Log($"Clipboard early restore not required: current sequence={operation.RestoreSnapshotSequence} already represents the latest external snapshot");
            }

            if (operation.Type != TextReplacementOperationType.LastWord)
            {
                operation.ConvertedText = LayoutConverter.Convert(operation.SourceText, sourceMap,
                    targetMap, out int unchangedCount);
                Log($"Conversion result: success=True, sourceLength={operation.SourceText.Length}, convertedLength={operation.ConvertedText.Length}, unchanged={unchangedCount}");
            }

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed before Clipboard write", targetWindow);
            if (operation.Type == TextReplacementOperationType.LastWord)
            {
                if (!ClipboardService.IsCurrentSequence(operation.LastWordSearchClipboardSequence))
                {
                    Log("CANCEL: Clipboard changed after LastWord search selection; replacement was not written");
                    return false;
                }
            }
            else if (!ClipboardService.IsCurrentSequence(operation.RestoreSnapshotSequence) &&
                     !TryAdoptCurrentClipboard(operation, "before Paste preparation"))
                return false;

            clipboardTimer.Restart();
            var writeTimer = Stopwatch.StartNew();
            if (!ClipboardService.TrySetText(operation.ConvertedText,
                    out uint pasteSequence))
            {
                Log($"FAIL: converted text Clipboard write timeout after {clipboardTimer.ElapsedMilliseconds} ms");
                return false;
            }
            if (operation.Type == TextReplacementOperationType.LastWord)
                Log($"LastWord PERF paste clipboardWrite={writeTimer.ElapsedMilliseconds} ms, t={total.ElapsedMilliseconds} ms");
            operation.OurPasteClipboardSequence = pasteSequence;
            operation.LastWordSearchClipboardContainsSourceText = false;
            clipboardContainsOurData = true;
            Log($"Converted text written to Clipboard: elapsed={clipboardTimer.ElapsedMilliseconds} ms, sequence={operation.OurPasteClipboardSequence}");

            if (GetForegroundWindow() != targetWindow)
                return Fail("active window changed immediately before Ctrl+V", targetWindow);
            var pasteTimer = Stopwatch.StartNew();
            if (!KeyboardInputService.Paste())
            {
                Log("FAIL: Ctrl+V SendInput failed");
                return false;
            }
            pasted = true;
            if (operation.Type == TextReplacementOperationType.LastWord)
                Log($"LastWord PERF paste CtrlV={pasteTimer.ElapsedMilliseconds} ms, t={total.ElapsedMilliseconds} ms");
            Log("Ctrl+V sent: success=True");

            Thread.Sleep(Math.Max(0, PasteRestoreDelayMilliseconds));
            var restoreTimer = Stopwatch.StartNew();
            ClipboardRestoreResult restoreResult = ClipboardService.RestoreIfUnchanged(
                operation.RestoreSnapshot, operation.OurPasteClipboardSequence, out _);
            LogRestoreResult(restoreResult, "after Paste");
            clipboardContainsOurData = false;
            if (operation.Type == TextReplacementOperationType.LastWord)
                Log($"LastWord PERF paste postPasteWait={PasteRestoreDelayMilliseconds} ms, clipboardRestore={restoreTimer.ElapsedMilliseconds} ms, t={total.ElapsedMilliseconds} ms");

            var switchTimer = Stopwatch.StartNew();
            bool layoutSwitched = KeyboardLayoutService.SwitchLayout(targetWindow, targetLayout);
            if (operation.Type == TextReplacementOperationType.LastWord)
                Log($"LastWord PERF layout-switch duration={switchTimer.ElapsedMilliseconds} ms, t={total.ElapsedMilliseconds} ms");
            Log($"Layout switch: success={layoutSwitched}, target={targetLayout.DisplayName}");
            if (!layoutSwitched)
                return false;

            Log("SUCCESS");
            if (operation.Type == TextReplacementOperationType.LastWord)
                Log($"LastWord PERF SUMMARY result=SUCCESS TOTAL={total.ElapsedMilliseconds} ms");
            return true;
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (clipboardContainsOurData || operation.LastWordSearchClipboardContainsSourceText)
                LogRestoreResult(ClipboardService.RestoreIfUnchanged(operation.RestoreSnapshot,
                    operation.OurPasteClipboardSequence != 0
                        ? operation.OurPasteClipboardSequence
                        : operation.LastWordSearchClipboardContainsSourceText
                            ? operation.LastWordSearchClipboardSequence
                            : operation.LastObservedClipboardSequence), "during cleanup");
            Log($"Operation result: operation={operation.Id:N}, pasted={pasted}, elapsed={total.ElapsedMilliseconds} ms");
            Log("========== END ==========" + Environment.NewLine);
            Volatile.Write(ref _running, 0);
        }
    }

    private static bool PrepareSelection(TextReplacementOperation operation, Stopwatch total)
    {
        switch (operation.Type)
        {
            case TextReplacementOperationType.FullText:
                if (!KeyboardInputService.SelectAll()) return false;
                Log("Ctrl+A sent");
                return true;
            case TextReplacementOperationType.LastWord:
                return TrySelectLastNonWhitespaceFragment(operation, total);
            case TextReplacementOperationType.SelectedText:
                Log("Existing selection retained");
                return true;
            default:
                return false;
        }
    }

    private static bool TrySelectLastNonWhitespaceFragment(TextReplacementOperation operation, Stopwatch total)
    {
        if (!TryProbeLastWordDirection(operation, total, out LastWordSearchDirection direction,
                out string selection))
        {
            Log("FAIL: no LastWord text found in either direction");
            return false;
        }

        Log($"LastWord search direction selected: {direction}");
        string? previousSelection = null;
        for (int iteration = 1; iteration <= MaxLastWordSearchIterations; iteration++)
        {
            if (GetForegroundWindow() != operation.TargetWindow)
                return Fail("active window changed during LastWord search", operation.TargetWindow);
            if (iteration > 1)
            {
                if (!KeyboardInputService.SelectPreviousWord(direction))
                    return false;
                Log($"LastWord search iteration={iteration}: Ctrl+Shift+{direction} sent");
                Thread.Sleep(15);

                if (!TryCopySearchSelection(operation, total, $"search iteration={iteration}", out selection))
                    return false;
            }

            var analysisTimer = Stopwatch.StartNew();
            LastWordSelectionAnalysis analysis = LastWordSelectionAnalyzer.Analyze(selection);
            Log($"LastWord PERF analyze-selection iteration={iteration}, duration={analysisTimer.ElapsedMilliseconds} ms, selectionLength={selection.Length}, t={total.ElapsedMilliseconds} ms");
            Log($"LastWord search iteration={iteration}, selectionLength={selection.Length}, boundaryWhitespaceFound={analysis.BoundaryWhitespaceFound}, trailingWhitespace={analysis.TrailingWhitespaceLength}");

            bool reachedStartOfField = analysis.HasFragment &&
                previousSelection != null && string.Equals(previousSelection, selection, StringComparison.Ordinal);
            if (analysis.BoundaryWhitespaceFound || reachedStartOfField)
            {
                string boundary = analysis.BoundaryWhitespaceFound ? "Whitespace" : "StartOfField";
                if (!PrepareDirectReplacement(operation, selection, analysis, boundary, total))
                    return false;
                Log($"LastWord search completed: iterations={iteration}, fragmentLength={analysis.FragmentLength}, trailingWhitespace={analysis.TrailingWhitespaceLength}, boundary={boundary}");
                return true;
            }

            previousSelection = selection;
        }

        Log($"FAIL: LastWord boundary was not found within {MaxLastWordSearchIterations} iterations");
        return false;
    }

    private static bool TryProbeLastWordDirection(TextReplacementOperation operation, Stopwatch total,
        out LastWordSearchDirection selectedDirection, out string selection)
    {
        selection = string.Empty;
        foreach (LastWordSearchDirection direction in new[]
                 { LastWordSearchDirection.Left, LastWordSearchDirection.Right })
        {
            Log($"LastWord direction probe: direction={direction}");
            var probeTimer = Stopwatch.StartNew();
            if (!KeyboardInputService.SelectPreviousWord(direction))
            {
                Log($"LastWord direction probe: {direction} SendInput failed");
                selectedDirection = default;
                return false;
            }
            Thread.Sleep(15);

            string marker = $"LayoutFixer_LastWordProbe_{Guid.NewGuid():N}";
            if (!ClipboardService.TrySetText(marker, out uint markerSequence))
            {
                Log("FAIL: LastWord probe marker could not be written to Clipboard");
                selectedDirection = default;
                return false;
            }
            operation.LastWordSearchClipboardSequence = markerSequence;
            operation.LastWordSearchClipboardContainsSourceText = true;
            Log("LastWord probe marker written");

            var copyTimer = Stopwatch.StartNew();
            if (!KeyboardInputService.Copy())
            {
                Log("FAIL: LastWord probe Ctrl+C SendInput failed");
                selectedDirection = default;
                return false;
            }

            if (ClipboardService.WaitForStableCopy(markerSequence, copyTimer,
                    out ClipboardCopyResult copyResult))
            {
                if (copyResult.Text.Length == 0 || string.Equals(copyResult.Text, marker,
                        StringComparison.Ordinal))
                {
                    Log($"LastWord probe result: indeterminate, elapsed={copyResult.ElapsedMilliseconds} ms");
                    selectedDirection = default;
                    return false;
                }

                RecordLastWordSearchCopy(operation, copyResult);
                LogLastWordCopyPerf($"direction-probe direction={direction}", copyResult, total);
                selection = copyResult.Text;
                Log($"LastWord probe result: TextCopied, selectionLength={selection.Length}");
                selectedDirection = direction;
                Log($"LastWord PERF direction-probe END direction={direction}, result=TextCopied, duration={probeTimer.ElapsedMilliseconds} ms, t={total.ElapsedMilliseconds} ms"); return true;
            }

            if (ClipboardService.IsCurrentSequence(markerSequence))
            {
                Log("LastWord probe result: NoCopiedSelection");
                continue;
            }

            Log($"LastWord probe result: indeterminate, elapsed={copyResult.ElapsedMilliseconds} ms");
            selectedDirection = default;
            return false;
        }

        selectedDirection = default;
        return false;
    }

    private static bool TryCopySearchSelection(TextReplacementOperation operation, Stopwatch total, string stage, out string selection)
    {
        selection = string.Empty;
        uint sequenceBeforeCopy = ClipboardService.SequenceNumber;
        var copyTimer = Stopwatch.StartNew();
        if (!KeyboardInputService.Copy())
        {
            Log("FAIL: LastWord search Ctrl+C SendInput failed");
            return false;
        }
        Log("LastWord search Ctrl+C sent");
        if (!ClipboardService.WaitForStableCopy(sequenceBeforeCopy, copyTimer,
                out ClipboardCopyResult copyResult))
        {
            Log($"FAIL: LastWord search Clipboard copy did not stabilize within {copyResult.ElapsedMilliseconds} ms");
            return false;
        }

        selection = copyResult.Text;
        RecordLastWordSearchCopy(operation, copyResult);
        LogLastWordCopyPerf(stage, copyResult, total);

        return true;
    }

    private static void RecordLastWordSearchCopy(TextReplacementOperation operation,
        ClipboardCopyResult copyResult)
    {
        operation.LastObservedClipboardSequence = copyResult.LastSequence;
        if (copyResult.NewExternalSnapshot != null)
        {
            operation.RestoreSnapshot = copyResult.NewExternalSnapshot;
            operation.RestoreSnapshotSequence = copyResult.NewExternalSnapshotSequence;
        }

        operation.LastWordSearchClipboardContainsSourceText = copyResult.ClipboardContainsSourceText;
        if (copyResult.ClipboardContainsSourceText)
        {
            operation.LastWordSearchClipboardSequence = copyResult.LastSequence;
            Log($"LastWord search Copy retained until search completes: sequence={copyResult.LastSequence}");
        }
    }

    private static bool PrepareDirectReplacement(TextReplacementOperation operation,
        string selection, LastWordSelectionAnalysis analysis, string boundary, Stopwatch total)
    {
        string prefix = selection[..analysis.FragmentStart];
        string fragment = selection.Substring(analysis.FragmentStart, analysis.FragmentLength);
        string suffix = selection[(analysis.FragmentStart + analysis.FragmentLength)..];
        var convertTimer = Stopwatch.StartNew();
        string converted = LayoutConverter.Convert(fragment, operation.SourceMap, operation.TargetMap,
            out _);
        string candidate = prefix + converted + suffix;
        bool canReplace = analysis.HasFragment && fragment.Length > 0 &&
            candidate.Length >= prefix.Length + suffix.Length;
        Log($"LastWord DIRECT-REPLACE: boundary={boundary}, searchSelectionLength={selection.Length}, prefixLength={prefix.Length}, fragmentLength={fragment.Length}, suffixLength={suffix.Length}, convertedFragmentLength={converted.Length}, candidateReplacementLength={candidate.Length}, canReplaceSearchSelection={canReplace}, searchSelectionHash={ClipboardService.HashText(selection)}, candidateReplacementHash={ClipboardService.HashText(candidate)}");
        Log($"LastWord PERF convert sourceLength={fragment.Length}, convertedLength={converted.Length}, duration={convertTimer.ElapsedMilliseconds} ms, t={total.ElapsedMilliseconds} ms");
        if (!canReplace)
        {
            Log("CANCEL: LastWord Direct Replace candidate is invalid");
            return false;
        }

        operation.LastWordSearchFragment = fragment;
        operation.LastWordCandidateReplacement = candidate;
        operation.LastWordSearchSelectionLength = selection.Length;
        operation.LastWordDirectReplaceReady = true;
        return true;
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

    private static void LogLastWordCopyPerf(string stage, ClipboardCopyResult result, Stopwatch total) =>
        Log($"LastWord PERF Clipboard {stage}: firstChangeDelay={result.FirstChangeDelayMilliseconds} ms, stabilizationDelay={result.StabilizationDelayMilliseconds} ms, totalCopyWait={result.ElapsedMilliseconds} ms, readText={result.ReadTextMilliseconds} ms, sequenceChanges={result.ChangeCount}, selectionLength={result.Text.Length}, t={total.ElapsedMilliseconds} ms");

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

