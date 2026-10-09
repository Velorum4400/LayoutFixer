using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

// TextPattern has no range-write operation. This helper uses its verified range only to
// select the exact fragment, then lets the target editor replace that selection normally.
internal static class UiaTargetedReplacement
{
    private const int VerificationTimeoutMilliseconds = 300;
    private const int VerificationPollMilliseconds = 5;

    internal static bool TryBuildPlan(string text, int caretOffset, string converted,
        out int start, out int length, out string fragment, out string expectedText,
        out int expectedCaret, out string reason)
    {
        start = length = expectedCaret = 0;
        fragment = expectedText = reason = string.Empty;
        if (caretOffset < 0 || caretOffset > text.Length) { reason = "CaretOutOfRange"; return false; }
        LastWordSelectionAnalysis analysis = LastWordSelectionAnalyzer.Analyze(text[..caretOffset]);
        if (!analysis.HasFragment) { reason = "NoFragmentBeforeCaret"; return false; }
        start = analysis.FragmentStart;
        length = analysis.FragmentLength;
        if (start < 0 || length <= 0 || start + length > caretOffset) { reason = "LastWordRangeOutOfBounds"; return false; }
        fragment = text.Substring(start, length);
        if (string.IsNullOrEmpty(converted) || string.Equals(fragment, converted, StringComparison.Ordinal))
        { reason = "ConversionUnchanged"; return false; }
        expectedText = text[..start] + converted + text[(start + length)..];
        expectedCaret = start + converted.Length;
        return true;
    }

    internal static bool TryReplace(TextReplacementOperation operation, AutomationElement element,
        TextPattern text, int[] runtimeId, string originalText, int start, int length,
        string fragment, string converted, string expectedText, int expectedCaret, out string result)
    {
        result = "TargetedReplacementFailed";
        var total = Stopwatch.StartNew();
        Log(operation, "NATIVE LASTWORD: handler=Chromium strategy=TargetedReplacement BEGIN");
        try
        {
            if (!Preflight(operation, element, runtimeId, text, originalText, out string preflightReason))
            { result = preflightReason; return false; }
            if (!TrySelectExactRange(operation, text, start, length, fragment, out string selectionReason))
            { result = selectionReason; return false; }
            if (!Preflight(operation, element, runtimeId, text, originalText, out preflightReason))
            { result = preflightReason; return false; }
            if (!KeyboardInputService.SendUnicodeText(converted))
            { result = "UnicodeInputFailed"; return false; }

            if (!WaitForExpectedState(text, expectedText, expectedCaret, out bool caretPreserved))
            { result = "TargetedReplacementUnverified"; return false; }

            LastWordLayoutResolver.RecordVerifiedConversion(operation, fragment, converted, start, expectedCaret);
            LayoutSwitchVerificationResult switchResult = KeyboardLayoutService.SwitchLayoutAndVerify(
                operation.TargetWindow, operation.TargetLayout, out _, out _);
            if (switchResult != LayoutSwitchVerificationResult.Success)
            { result = "TargetedReplacementSucceededLayoutSwitchFailed"; return true; }
            Log(operation, $"NATIVE LASTWORD: handler=Chromium strategy=TargetedReplacement fragment=\"{Escape(fragment)}\" converted=\"{Escape(converted)}\" result=Success caretPreserved={caretPreserved}");
            result = "ChromiumTargetedReplacementSucceeded";
            return true;
        }
        catch (ElementNotAvailableException) { result = "ElementNotAvailable"; return false; }
        catch (Exception ex) { result = ex.GetType().Name; return false; }
        finally
        {
            Log(operation, $"NATIVE LASTWORD: handler=Chromium strategy=TargetedReplacement END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    private static bool Preflight(TextReplacementOperation operation, AutomationElement element, int[] runtimeId,
        TextPattern text, string expectedText, out string reason)
    {
        reason = string.Empty;
        if (TextReplacementService.ForegroundWindow != operation.TargetWindow) { reason = "TargetChangedBeforeTargetedReplacement"; return false; }
        if (!SameRuntimeId(AutomationElement.FocusedElement, runtimeId)) { reason = "FocusChangedBeforeTargetedReplacement"; return false; }
        if (!string.Equals(text.DocumentRange.GetText(-1), expectedText, StringComparison.Ordinal))
        { reason = "TextChangedBeforeTargetedReplacement"; return false; }
        return true;
    }

    private static bool TrySelectExactRange(TextReplacementOperation operation, TextPattern text, int start,
        int length, string expected, out string reason)
    {
        reason = "TargetedSelectionFailed";
        try
        {
            TextPatternRange document = text.DocumentRange;
            TextPatternRange range = document.Clone();
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.Start);
            if (range.Move(TextUnit.Character, start) != start) { reason = "TargetedSelectionStartMoveFailed"; return false; }
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, length) != length)
            { reason = "TargetedSelectionEndMoveFailed"; return false; }
            if (!string.Equals(range.GetText(-1), expected, StringComparison.Ordinal))
            { reason = "TargetedSelectionTextMismatch"; return false; }
            range.Select();
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds <= VerificationTimeoutMilliseconds)
            {
                TextPatternRange[] selection = text.GetSelection();
                if (selection.Length == 1 &&
                    Offset(text, selection[0], TextPatternRangeEndpoint.Start) == start &&
                    Offset(text, selection[0], TextPatternRangeEndpoint.End) == start + length &&
                    string.Equals(selection[0].GetText(-1), expected, StringComparison.Ordinal))
                    return true;
                Thread.Sleep(VerificationPollMilliseconds);
            }
            reason = "TargetedSelectionVerificationFailed";
            return false;
        }
        catch (Exception ex) { reason = "TargetedSelection" + ex.GetType().Name; return false; }
    }

    private static bool WaitForExpectedState(TextPattern text, string expectedText, int expectedCaret,
        out bool caretPreserved)
    {
        caretPreserved = false;
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds <= VerificationTimeoutMilliseconds)
        {
            try
            {
                if (string.Equals(text.DocumentRange.GetText(-1), expectedText, StringComparison.Ordinal))
                {
                    TextPatternRange[] selection = text.GetSelection();
                    caretPreserved = selection.Length == 1 &&
                        selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.End) == 0 &&
                        Offset(text, selection[0], TextPatternRangeEndpoint.Start) == expectedCaret;
                    return caretPreserved;
                }
            }
            catch { return false; }
            Thread.Sleep(VerificationPollMilliseconds);
        }
        return false;
    }

    private static int Offset(TextPattern text, TextPatternRange range, TextPatternRangeEndpoint endpoint)
    {
        TextPatternRange prefix = text.DocumentRange.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, endpoint);
        return prefix.GetText(-1).Length;
    }

    private static bool SameRuntimeId(AutomationElement element, int[] expected)
    {
        try { return element.GetRuntimeId().SequenceEqual(expected); } catch { return false; }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r")
        .Replace("\n", "\\n").Replace("\t", "\\t");
    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

