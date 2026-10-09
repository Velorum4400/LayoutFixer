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
        => TryReplace(operation, element, text, runtimeId, originalText, start, length, fragment, converted,
            expectedText, expectedCaret, "Chromium", "ChromiumTargetedReplacementSucceeded", out result);

    internal static bool TryReplace(TextReplacementOperation operation, AutomationElement element,
        TextPattern text, int[] runtimeId, string originalText, int start, int length,
        string fragment, string converted, string expectedText, int expectedCaret, string handlerName,
        string successResult, out string result)
    {
        result = "TargetedReplacementFailed";
        var total = Stopwatch.StartNew();
        Log(operation, $"NATIVE LASTWORD: handler={handlerName} strategy=TargetedReplacement BEGIN");
        try
        {
            if (!Preflight(operation, element, runtimeId, text, originalText, out string preflightReason))
            { result = preflightReason; return false; }
            var selectionTimer = Stopwatch.StartNew();
            if (!TrySelectExactRange(operation, text, start, length, fragment, handlerName, out string selectionReason))
            { result = selectionReason; return false; }
            Log(operation, $"{handlerName} PERF SelectionMs={selectionTimer.ElapsedMilliseconds}");
            if (!Preflight(operation, element, runtimeId, text, originalText, out preflightReason))
            { result = preflightReason; return false; }
            var replacementTimer = Stopwatch.StartNew();
            if (!KeyboardInputService.SendUnicodeText(converted))
            { result = "UnicodeInputFailed"; return false; }
            Log(operation, $"{handlerName} PERF ReplacementMs={replacementTimer.ElapsedMilliseconds}");

            var verificationTimer = Stopwatch.StartNew();
            if (!WaitForExpectedState(operation, text, expectedText, expectedCaret, handlerName, out bool caretPreserved))
            { result = "TargetedReplacementUnverified"; return false; }
            Log(operation, $"{handlerName} PERF VerificationMs={verificationTimer.ElapsedMilliseconds}");

            LastWordLayoutResolver.RecordVerifiedConversion(operation, fragment, converted, start, expectedCaret);
            LayoutSwitchVerificationResult switchResult = KeyboardLayoutService.SwitchLayoutAndVerify(
                operation.TargetWindow, operation.TargetLayout, out _, out _);
            if (switchResult != LayoutSwitchVerificationResult.Success)
            { result = "TargetedReplacementSucceededLayoutSwitchFailed"; return true; }
            Log(operation, $"NATIVE LASTWORD: handler={handlerName} strategy=TargetedReplacement fragment=\"{Escape(fragment)}\" converted=\"{Escape(converted)}\" result=Success caretPreserved={caretPreserved}");
            result = successResult;
            return true;
        }
        catch (ElementNotAvailableException) { result = "ElementNotAvailable"; return false; }
        catch (Exception ex) { result = ex.GetType().Name; return false; }
        finally
        {
            Log(operation, $"NATIVE LASTWORD: handler={handlerName} strategy=TargetedReplacement END result={result} durationMs={total.ElapsedMilliseconds}");
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
        int length, string expected, string handlerName, out string reason)
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
            Log(operation, $"{handlerName} SelectionMethod=TextPatternRange.Select SelectionAttempted=True ExpectedSelectionStart={start} ExpectedSelectionEnd={start + length} ExpectedSelectionText=\"{Escape(expected)}\"");
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds <= VerificationTimeoutMilliseconds)
            {
                TextPatternRange[] selection = text.GetSelection();
                if (selection.Length == 1)
                {
                    int actualStart = Offset(text, selection[0], TextPatternRangeEndpoint.Start);
                    int actualEnd = Offset(text, selection[0], TextPatternRangeEndpoint.End);
                    string actualText = selection[0].GetText(-1);
                    if (actualStart == start && actualEnd == start + length &&
                        string.Equals(actualText, expected, StringComparison.Ordinal))
                    {
                        Log(operation, $"{handlerName} ActualSelectionStart={actualStart} ActualSelectionEnd={actualEnd} ActualSelectionText=\"{Escape(actualText)}\" SelectionVerified=True");
                        return true;
                    }
                }
                Thread.Sleep(VerificationPollMilliseconds);
            }
            reason = "TargetedSelectionVerificationFailed";
            return false;
        }
        catch (Exception ex) { reason = "TargetedSelection" + ex.GetType().Name; return false; }
    }

    private static bool WaitForExpectedState(TextReplacementOperation operation, TextPattern text, string expectedText,
        int expectedCaret, string handlerName, out bool caretPreserved)
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
                    Log(operation, $"{handlerName} ReplacementVerified=True CaretVerified={caretPreserved}");
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

