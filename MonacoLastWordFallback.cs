using System;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

internal static class MonacoLastWordFallback
{
    internal static bool IsMonacoEditor(AutomationElement element)
    {
        try
        {
            if (!IsMonacoClassPair(element.Current.ClassName, TreeWalker.ControlViewWalker.GetParent(element)?.Current.ClassName)) return false;
            return true;
        }
        catch { return false; }
    }

    internal static bool IsMonacoClassPair(string? elementClass, string? parentClass) =>
        string.Equals(elementClass, "native-edit-context", StringComparison.Ordinal) &&
        parentClass?.Contains("monaco-editor", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsAccessibleDocumentText(string text) =>
        !text.Contains("screen reader", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("accessibility support", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("not available", StringComparison.OrdinalIgnoreCase);

    internal static bool TryReplace(TextReplacementOperation operation, AutomationElement element, TextPattern text,
        string originalText, string replacement, int start, int length, int expectedCaret, out string result)
    {
        result = "MonacoReplacementFailed"; var total = Stopwatch.StartNew(); ClipboardSnapshot snapshot = new(); uint pasteSequence = 0;
        Log(operation, "MONACO LASTWORD BEGIN");
        try
        {
            Log(operation, "MonacoDetected=True ScreenReaderModeAccessible=True TextRead=True CaretRead=True LastWordRangeValid=True");
            if (TextReplacementService.ForegroundWindow != operation.TargetWindow) { result = "MonacoFocusChanged"; return false; }
            if (!TrySelect(text, start, length, out string selectionReason)) { result = selectionReason; return false; }
            Log(operation, "SelectionAttempted=True SelectionVerified=True");
            if (!ClipboardService.TryCaptureStable(out snapshot, out _, out _)) { result = "MonacoClipboardCaptureFailed"; return false; }
            if (!ClipboardService.TrySetText(replacement.Substring(start, expectedCaret - start), out pasteSequence)) { result = "MonacoClipboardWriteFailed"; return false; }
            if (TextReplacementService.ForegroundWindow != operation.TargetWindow) { result = "MonacoFocusChanged"; return false; }
            if (!KeyboardInputService.Paste()) { result = "MonacoReplacementFailed"; return false; }
            Log(operation, "ReplacementAttempted=True");
            if (!WaitForText(text, replacement)) { result = "MonacoReplacementUnverified"; return false; }
            Log(operation, "ReplacementVerified=True");
            if (!VerifyCaret(text, expectedCaret)) { result = "MonacoReplacementSucceededCaretRestoreFailed"; return true; }
            Log(operation, "CaretRestoreVerified=True");
            LayoutSwitchVerificationResult switchResult = KeyboardLayoutService.SwitchLayoutAndVerify(operation.TargetWindow,
                operation.TargetLayout, out _, out _);
            if (switchResult != LayoutSwitchVerificationResult.Success) { result = "MonacoReplacementSucceededLayoutSwitchFailed"; return true; }
            result = "MonacoLastWordReplaced"; return true;
        }
        catch (Exception ex) { Log(operation, $"Monaco failure={ex.GetType().Name}"); return false; }
        finally
        {
            if (pasteSequence != 0) ClipboardService.RestoreIfUnchanged(snapshot, pasteSequence);
            Log(operation, $"MONACO LASTWORD END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    private static bool TrySelect(TextPattern text, int start, int length, out string reason)
    {
        reason = "MonacoSelectionFailed";
        try
        {
            TextPatternRange doc = text.DocumentRange, range = doc.Clone();
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, doc, TextPatternRangeEndpoint.Start);
            if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0) return false;
            if (range.Move(TextUnit.Character, start) != start) return false;
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, length) != length) return false;
            range.Select();
            TextPatternRange[] selection = text.GetSelection();
            if (selection.Length != 1 || selection[0].GetText(-1).Length != length) { reason = "MonacoSelectionVerificationFailed"; return false; }
            return true;
        }
        catch { return false; }
    }

    private static bool WaitForText(TextPattern text, string expected)
    {
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds <= 300)
        {
            try { if (string.Equals(text.DocumentRange.GetText(-1), expected, StringComparison.Ordinal)) return true; } catch { return false; }
            Thread.Sleep(5);
        }
        return false;
    }

    private static bool VerifyCaret(TextPattern text, int expected)
    {
        try
        {
            TextPatternRange[] selection = text.GetSelection();
            if (selection.Length != 1 || selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.End) != 0) return false;
            TextPatternRange prefix = text.DocumentRange.Clone();
            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
            return prefix.GetText(-1).Length == expected;
        }
        catch { return false; }
    }

    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

