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
            bool postHasValue = TryPattern(element, ValuePattern.Pattern, out ValuePattern? post);
            string actual = postHasValue && post != null ? post.Current.Value : string.Empty;
            bool match = postHasValue && string.Equals(actual, replacement, StringComparison.Ordinal);
            Log(operation, $"UiaWholeValue postWriteExpectedLength={replacement.Length} postWriteActualLength={actual.Length} postWriteValueMatch={match}");
            if (!match) { result = "PostWriteValueMismatch"; return false; }
            InspectCaret(operation, element, expectedCaretOffset);
            Log(operation, "UiaWholeValue manualUndoTestRequired=True");
            Log(operation, "UiaWholeValue manualEditorStateTestRequired=True");
            result = "UiaWholeValueReplaced"; return true;
        }
        catch (Exception ex) { result = ex.GetType().Name; Log(operation, $"UiaWholeValue failure={result}"); return false; }
        finally { Log(operation, $"UIA VALUE REPLACEMENT END result={result} durationMs={Ms(timer)}"); }
    }

    private static void InspectCaret(TextReplacementOperation operation, AutomationElement element, int expected)
    {
        try
        {
            if (!TryPattern(element, TextPattern.Pattern, out TextPattern? text) || text == null) { Log(operation, "UiaWholeValue postWriteCaretReadable=False reason=NoTextPattern"); return; }
            string document = text.DocumentRange.GetText(-1); TextPatternRange[] ranges = text.GetSelection();
            bool readable = ranges.Length == 1 && ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start, ranges[0], TextPatternRangeEndpoint.End) == 0;
            int offset = -1;
            if (readable) { var prefix = text.DocumentRange.Clone(); prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, ranges[0], TextPatternRangeEndpoint.Start); offset = prefix.GetText(-1).Length; }
            Log(operation, $"UiaWholeValue postWriteTextLength={document.Length} postWriteSelectionCount={ranges.Length} postWriteCaretReadable={readable} postWriteCaretOffset={offset} caretPreserved={(readable && offset == expected)}");
        }
        catch (Exception ex) { Log(operation, $"UiaWholeValue postWriteCaretReadable=False reason={ex.GetType().Name}"); }
    }

    private static bool SameRuntimeId(AutomationElement element, int[] expected) { try { return expected.SequenceEqual(element.GetRuntimeId()); } catch { return false; } }
    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T? value) where T : class { value = null; try { value = element.GetCurrentPattern(pattern) as T; return value != null; } catch { return false; } }
    private static string Ms(Stopwatch timer) => (timer.ElapsedTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms";

    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

