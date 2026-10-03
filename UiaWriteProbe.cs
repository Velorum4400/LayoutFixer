using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

// Whole-value UIA experiment. It never changes a TextPattern selection.
internal static class UiaWriteProbe
{
    internal const int MaximumValueLength = 4096;
    internal const int PostSetValueTimeoutMs = 150;
    internal const int PostSetValuePollIntervalMs = 5;

    internal static bool TryBuildReplacement(string fullText, int caretOffset, string converted,
        out int lastWordStart, out int lastWordEnd, out string fragment, out string replacement,
        out int expectedCaretOffset, out string reason)
    {
        lastWordStart = lastWordEnd = expectedCaretOffset = 0;
        fragment = replacement = reason = string.Empty;
        if (caretOffset < 0 || caretOffset > fullText.Length) { reason = "CaretOutOfRange"; return false; }
        var analysis = LastWordSelectionAnalyzer.Analyze(fullText[..caretOffset]);
        if (!analysis.HasFragment) { reason = "NoFragmentBeforeCaret"; return false; }
        lastWordStart = analysis.FragmentStart;
        lastWordEnd = lastWordStart + analysis.FragmentLength;
        if (lastWordEnd > caretOffset || lastWordEnd > fullText.Length) { reason = "LastWordRangeOutOfBounds"; return false; }
        fragment = fullText.Substring(lastWordStart, analysis.FragmentLength);
        if (fragment.Length == 0 || string.Equals(fragment, converted, StringComparison.Ordinal)) { reason = "ConversionUnchanged"; return false; }
        replacement = fullText[..lastWordStart] + converted + fullText[lastWordEnd..];
        expectedCaretOffset = lastWordStart + converted.Length;
        return true;
    }

    internal static bool TryReplace(TextReplacementOperation operation, AutomationElement element,
        int[] runtimeId, string originalValue, string replacement, int expectedCaretOffset, out string result)
    {
        var timer = Stopwatch.StartNew(); result = "Aborted";
        Log(operation, "UIA VALUE REPLACEMENT BEGIN");
        try
        {
            bool foreground = TextReplacementService.ForegroundWindow == operation.TargetWindow;
            bool focus = SameRuntimeId(AutomationElement.FocusedElement, runtimeId);
            bool hasValue = TryPattern(element, ValuePattern.Pattern, out ValuePattern? value);
            bool writable = hasValue && value != null && !value.Current.IsReadOnly;
            string current = writable ? value!.Current.Value : string.Empty;
            bool unchanged = writable && string.Equals(current, originalValue, StringComparison.Ordinal);
            Log(operation, $"UiaWholeValue preWriteForegroundValid={foreground} preWriteFocusValid={focus} preWriteValueWritable={writable} preWriteValueUnchanged={unchanged}");
            if (!foreground) { result = "TargetChangedBeforeWrite"; return false; }
            if (!focus) { result = "FocusChangedBeforeWrite"; return false; }
            if (!writable) { result = "ValuePatternNotWritableBeforeWrite"; return false; }
            if (!unchanged) { result = "TextChangedBeforeWrite"; return false; }
            var set = Stopwatch.StartNew(); value!.SetValue(replacement);
            Log(operation, $"UiaWholeValue SetValue attempted=True elapsedMs={Ms(set)}");
            if (!WaitForValue(operation, runtimeId, replacement, out AutomationElement refreshed, out string stableReason))
            { result = "UiaWholeValueReplaced" + stableReason; return true; }
            if (!RestoreCaret(operation, refreshed, runtimeId, replacement, expectedCaretOffset, out string caretReason))
            { result = "UiaWholeValueReplacedCaretRestoreFailed"; Log(operation, $"UiaWholeValue caretRestoreFailureReason={caretReason}"); return true; }
            Log(operation, "UiaWholeValue manualUndoTestRequired=True");
            Log(operation, "UiaWholeValue manualEditorStateTestRequired=True");
            result = "UiaWholeValueReplacedAndCaretRestored"; return true;
        }
        catch (Exception ex) { result = ex.GetType().Name; Log(operation, $"UiaWholeValue failure={result}"); return false; }
        finally { Log(operation, $"UIA VALUE REPLACEMENT END result={result} durationMs={Ms(timer)}"); }
    }

    private static bool WaitForValue(TextReplacementOperation operation, int[] runtimeId, string expected,
        out AutomationElement refreshed, out string reason)
    {
        var timer = Stopwatch.StartNew(); refreshed = null!; reason = string.Empty;
        Log(operation, $"UIA POST-WRITE STABILIZATION BEGIN timeoutMs={PostSetValueTimeoutMs} pollIntervalMs={PostSetValuePollIntervalMs}");
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                bool same = SameRuntimeId(focused, runtimeId);
                ValuePattern? value = null;
                bool hasValue = same && TryPattern(focused, ValuePattern.Pattern, out value);
                string actual = hasValue && value != null ? value.Current.Value : string.Empty;
                bool match = hasValue && string.Equals(actual, expected, StringComparison.Ordinal);
                Log(operation, $"PostWritePoll attempt={attempt} elapsedMs={timer.ElapsedMilliseconds} sameElement={same} actualLength={actual.Length} match={match}");
                if (match) { refreshed = focused; Log(operation, $"UiaWholeValue postWriteExpectedLength={expected.Length} postWriteActualLength={actual.Length} postWriteValueMatch=True"); Log(operation, $"UIA POST-WRITE STABILIZATION END result=Stable durationMs={Ms(timer)}"); return true; }
            }
            catch (Exception ex) { Log(operation, $"PostWritePoll attempt={attempt} failure={ex.GetType().Name}"); }
            if (timer.ElapsedMilliseconds >= PostSetValueTimeoutMs) break;
            Thread.Sleep(PostSetValuePollIntervalMs);
        }
        reason = "CaretRestoreSkippedPostWriteStabilizationTimeout";
        Log(operation, $"UiaWholeValue postWriteValueMatch=False reason=PostWriteStabilizationTimeout");
        Log(operation, $"UIA POST-WRITE STABILIZATION END result=Timeout durationMs={Ms(timer)}");
        return false;
    }

    private static bool RestoreCaret(TextReplacementOperation operation, AutomationElement element, int[] runtimeId,
        string expectedValue, int expectedOffset, out string reason)
    {
        var timer = Stopwatch.StartNew(); reason = string.Empty;
        Log(operation, "UIA CARET RESTORE BEGIN");
        try
        {
            if (!TryPattern(element, TextPattern.Pattern, out TextPattern? text) || text == null) { reason = "NoTextPattern"; return false; }
            string documentText = text.DocumentRange.GetText(-1);
            Log(operation, $"UiaWholeValue postWriteTextPatternRefreshed=True documentLength={documentText.Length}");
            if (expectedOffset < 0 || expectedOffset > documentText.Length) { reason = "ExpectedCaretOutOfRange"; return false; }
            int natural = GetCaretOffset(text, out bool naturalReadable);
            bool needed = !naturalReadable || natural != expectedOffset;
            Log(operation, $"UiaWholeValue expectedCaretOffset={expectedOffset} naturalCaretOffset={natural} naturalCaretMatchesExpected={(naturalReadable && natural == expectedOffset)} caretRestoreNeeded={needed}");
            if (!needed) return true;
            TextPatternRange candidate = text.DocumentRange.Clone();
            candidate.MoveEndpointByRange(TextPatternRangeEndpoint.End, candidate, TextPatternRangeEndpoint.Start);
            candidate.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, expectedOffset);
            int resolved = GetOffset(text, candidate);
            bool degenerate = candidate.CompareEndpoints(TextPatternRangeEndpoint.Start, candidate, TextPatternRangeEndpoint.End) == 0;
            Log(operation, $"UiaWholeValue caretRangeTargetOffset={expectedOffset} caretRangeResolvedOffset={resolved} caretRangeDegenerate={degenerate}");
            if (!degenerate || resolved != expectedOffset) { reason = "CaretRangeResolutionMismatch"; return false; }
            bool foreground = TextReplacementService.ForegroundWindow == operation.TargetWindow;
            bool focus = SameRuntimeId(AutomationElement.FocusedElement, runtimeId);
            bool hasValue = TryPattern(element, ValuePattern.Pattern, out ValuePattern? value);
            bool unchanged = hasValue && value != null && string.Equals(value.Current.Value, expectedValue, StringComparison.Ordinal);
            Log(operation, $"UiaWholeValue preSelectFocusValid={focus && foreground} preSelectValueUnchanged={unchanged}");
            if (!foreground || !focus) { reason = "FocusLost"; return false; }
            if (!unchanged) { reason = "TextChangedBeforeCaretRestore"; return false; }
            var select = Stopwatch.StartNew(); candidate.Select();
            Log(operation, $"UiaWholeValue CaretRestore Select attempted=True elapsedMs={Ms(select)}");
            if (!TryPattern(element, TextPattern.Pattern, out TextPattern? after) || after == null) { reason = "NoTextPatternAfterSelect"; return false; }
            int actual = GetCaretOffset(after, out bool readable);
            bool match = readable && actual == expectedOffset;
            Log(operation, $"UiaWholeValue postRestoreCaretOffset={actual} caretRestoreMatch={match}");
            if (!match) { reason = "UnexpectedCaretOffset"; return false; }
            return true;
        }
        catch (Exception ex) { reason = ex.GetType().Name == "ElementNotAvailableException" ? "StaleElement" : "SelectThrewException"; Log(operation, $"UiaWholeValue caretRestoreFailure={ex.GetType().Name}"); return false; }
        finally { Log(operation, $"UIA CARET RESTORE END durationMs={Ms(timer)}"); }
    }

    private static int GetCaretOffset(TextPattern text, out bool readable)
    {
        readable = false; TextPatternRange[] ranges = text.GetSelection();
        if (ranges.Length != 1 || ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start, ranges[0], TextPatternRangeEndpoint.End) != 0) return -1;
        readable = true; return GetOffset(text, ranges[0]);
    }
    private static int GetOffset(TextPattern text, TextPatternRange range)
    { var prefix = text.DocumentRange.Clone(); prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start); return prefix.GetText(-1).Length; }

    private static bool SameRuntimeId(AutomationElement element, int[] expected) { try { return expected.SequenceEqual(element.GetRuntimeId()); } catch { return false; } }
    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T? value) where T : class { value = null; try { value = element.GetCurrentPattern(pattern) as T; return value != null; } catch { return false; } }
    private static string Ms(Stopwatch timer) => (timer.ElapsedTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms";

    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

