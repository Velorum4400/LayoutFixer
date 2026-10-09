using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

// Phase 1 only. CoreWindow is a host class used by more than Windows Search, so this probe
// deliberately records capabilities and never selects, writes, or sends keyboard input.
internal static class WindowsSearchLastWordHandler
{
    private const int MaxDepth = 6;
    private const int MaxElements = 160;
    private const int BudgetMilliseconds = 400;
    private const int MaximumLoggedTextCharacters = 512;

    internal static bool TryReplace(TextReplacementOperation operation, IntPtr foregroundWindow,
        uint foregroundProcessId, IntPtr focusedWindow, uint focusedProcessId, out string result)
    {
        result = "WindowsSearchNotDetected";
        var total = Stopwatch.StartNew();
        Log(operation, "WINDOWS SEARCH LASTWORD BEGIN");
        try
        {
            var detection = Stopwatch.StartNew();
            if (foregroundWindow != operation.TargetWindow || foregroundProcessId == 0 ||
                focusedProcessId != foregroundProcessId)
            { result = "WindowsSearchFocusChanged"; return false; }

            AutomationElement focused = AutomationElement.FocusedElement;
            AutomationElement? target = FindFocusedCandidate(focused,
                FindCandidates(AutomationElement.FromHandle(foregroundWindow), foregroundProcessId), foregroundProcessId);
            if (target == null) { result = "WindowsSearchEditNotFound"; return false; }
            if (!IsConfirmedSearchTextBox(target, foregroundProcessId)) { result = "WindowsSearchNotDetected"; return false; }
            if (!TryPattern(target, TextPattern.Pattern, out TextPattern? text) || text == null)
            { result = "WindowsSearchTextUnavailable"; return false; }
            Log(operation, $"DetectionMs={detection.ElapsedMilliseconds}");

            var textRead = Stopwatch.StartNew();
            string documentText = text.DocumentRange.GetText(-1);
            LogText(operation, "DocumentText", documentText);
            Log(operation, $"DocumentLength={documentText.Length} TextReadMs={textRead.ElapsedMilliseconds}");
            var caretRead = Stopwatch.StartNew();
            TextPatternRange[] ranges = text.GetSelection();
            if (ranges.Length != 1) { result = "WindowsSearchCaretUnavailable"; return false; }
            int selectionStart = Offset(text, ranges[0], TextPatternRangeEndpoint.Start);
            int selectionEnd = Offset(text, ranges[0], TextPatternRangeEndpoint.End);
            Log(operation, $"SelectionCount={ranges.Length} SelectionStart={selectionStart} SelectionEnd={selectionEnd}");
            if (selectionStart != selectionEnd) { result = "WindowsSearchCaretUnavailable"; return false; }
            int caret = selectionStart;
            if (caret < 0 || caret > documentText.Length) { result = "WindowsSearchCaretUnavailable"; return false; }
            Log(operation, $"CaretOffset={caret} CaretReadMs={caretRead.ElapsedMilliseconds}");

            var wordDetection = Stopwatch.StartNew();
            TextDirection direction = DetectTextDirection(documentText, caret);
            LastWordSelectionAnalysis preceding = LastWordSelectionAnalyzer.Analyze(documentText[..caret]);
            Log(operation, $"TextDirection={direction}");
            Log(operation, $"CaretOffsetBefore={caret}");
            Log(operation, "CaretAdjustmentRequired=False");
            Log(operation, $"CaretOffsetAfterAdjustment={caret}");
            Log(operation, preceding.HasFragment
                ? $"WordRangeBefore={preceding.FragmentStart}-{preceding.FragmentStart + preceding.FragmentLength}"
                : "WordRangeBefore=none");
            if (!TryResolveWordRange(documentText, caret, direction, out int wordStart, out int wordLength,
                    out string rangeReason))
            { result = "WindowsSearchWordNotFound"; return false; }
            string fragment = documentText.Substring(wordStart, wordLength);
            Log(operation, $"WordRangeAfter={wordStart}-{wordStart + wordLength} reason={rangeReason}");
            LogText(operation, "WordText", fragment);
            Log(operation, $"WordDetectionMs={wordDetection.ElapsedMilliseconds}");
            string converted = LayoutConverter.Convert(fragment, operation.SourceMap, operation.TargetMap, out _);
            Log(operation, $"ConversionSource={operation.SourceLayout.ShortName} ConversionTarget={operation.TargetLayout.ShortName}");
            LogText(operation, "Converted", converted);
            if (LastWordLayoutOnlyCompletion.IsUnchanged(fragment, converted))
                return LastWordLayoutOnlyCompletion.TryComplete(operation, "WindowsSearch", out result);

            string expectedDocument = documentText[..wordStart] + converted + documentText[(wordStart + wordLength)..];
            int expectedCaret = wordStart + converted.Length;

            bool replaced = UiaTargetedReplacement.TryReplace(operation, target, text, target.GetRuntimeId(),
                documentText, wordStart, wordLength, fragment, converted, expectedDocument, expectedCaret,
                "WindowsSearch", "WindowsSearchSuccess", out string replacementResult);
            result = MapReplacementResult(replacementResult);
            if (replaced && result == "WindowsSearchSuccess")
                Log(operation, "LayoutSwitchVerified=True");
            return replaced;
        }
        catch (ElementNotAvailableException) { result = "WindowsSearchFocusChanged"; return false; }
        catch (Exception ex) { result = "WindowsSearch" + ex.GetType().Name; return false; }
        finally
        {
            Log(operation, $"WINDOWS SEARCH LASTWORD END result={result} TotalMs={total.ElapsedMilliseconds}");
        }
    }

    internal static bool Probe(TextReplacementOperation operation, IntPtr foregroundWindow, uint foregroundProcessId,
        IntPtr focusedWindow, uint focusedProcessId, out string result)
    {
        result = "WindowsSearchNotDetected";
        var total = Stopwatch.StartNew();
        Log(operation, "WINDOWS SEARCH UIA PROBE BEGIN");
        try
        {
            Log(operation, $"ForegroundClass=Windows.UI.Core.CoreWindow ForegroundWindow=0x{foregroundWindow.ToInt64():X} ForegroundProcess={foregroundProcessId}");
            Log(operation, $"FocusedWindow=0x{focusedWindow.ToInt64():X} FocusedWindowProcess={focusedProcessId}");
            AutomationElement root = AutomationElement.FromHandle(foregroundWindow);
            AutomationElement focused = AutomationElement.FocusedElement;
            LogElement(operation, "Root", root, 0);
            LogElement(operation, "FocusedElement", focused, 0);

            var candidates = FindCandidates(root, foregroundProcessId);
            Log(operation, $"CandidateCount={candidates.Count}");
            for (int index = 0; index < candidates.Count; index++)
                LogElement(operation, $"Candidate[{index}]", candidates[index], 0);

            AutomationElement? target = FindFocusedCandidate(focused, candidates, foregroundProcessId);
            if (target == null)
            {
                result = candidates.Count == 0 ? "WindowsSearchEditNotFound" : "WindowsSearchNotDetected";
                return false;
            }

            bool hasValue = TryPattern(target, ValuePattern.Pattern, out ValuePattern? value);
            bool hasText = TryPattern(target, TextPattern.Pattern, out TextPattern? text);
            // The .NET UIAutomationClient reference used by LayoutFixer does not expose
            // LegacyIAccessiblePattern or TextPattern2. Keep this explicit in the probe.
            bool hasLegacy = false;
            bool textReadable = false, selectionReadable = false, caretReadable = false;
            if (hasText && text != null)
            {
                try
                {
                    _ = text.DocumentRange.GetText(-1); // Capability only; never log user query text.
                    textReadable = true;
                    TextPatternRange[] selection = text.GetSelection();
                    selectionReadable = true;
                    caretReadable = selection.Length == 1 && selection[0].CompareEndpoints(
                        TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.End) == 0;
                }
                catch { }
            }
            bool writeSupported = hasValue && value != null && !value.Current.IsReadOnly;
            Log(operation, $"FocusedEditVerified=True ValuePattern={hasValue} TextPattern={hasText} TextPattern2=False LegacyIAccessible={hasLegacy} IsReadOnly={(hasValue && value != null ? value.Current.IsReadOnly : true)}");
            Log(operation, $"TextReadable={textReadable} CaretReadable={caretReadable} SelectionReadable={selectionReadable} WriteSupported={writeSupported}");
            result = !textReadable ? "WindowsSearchTextUnavailable" :
                !caretReadable ? "WindowsSearchCaretUnavailable" : "WindowsSearchDiagnosticsCaptured";
            Log(operation, "strategy=DiagnosticsOnly replacementAttempted=False");
            return false;
        }
        catch (ElementNotAvailableException) { result = "WindowsSearchFocusChanged"; return false; }
        catch (Exception ex) { result = "WindowsSearchProbe" + ex.GetType().Name; return false; }
        finally
        {
            Log(operation, $"WINDOWS SEARCH UIA PROBE END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    private static List<AutomationElement> FindCandidates(AutomationElement root, uint processId)
    {
        var result = new List<AutomationElement>();
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        var watch = Stopwatch.StartNew();
        try
        {
            AutomationElement? child = TreeWalker.RawViewWalker.GetFirstChild(root);
            if (child != null) pending.Push((child, 1));
        }
        catch { return result; }
        int visited = 0;
        while (pending.Count > 0 && visited < MaxElements && watch.ElapsedMilliseconds <= BudgetMilliseconds)
        {
            (AutomationElement element, int depth) = pending.Pop();
            visited++;
            try
            {
                AutomationElement? sibling = TreeWalker.RawViewWalker.GetNextSibling(element);
                if (sibling != null) pending.Push((sibling, depth));
                bool sameProcess = Get(element, AutomationElement.ProcessIdProperty, 0) == (int)processId;
                bool focused = Get(element, AutomationElement.HasKeyboardFocusProperty, false);
                bool editable = Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom) == ControlType.Edit ||
                    TryPattern(element, ValuePattern.Pattern, out ValuePattern? _) ||
                    TryPattern(element, TextPattern.Pattern, out TextPattern? _);
                if (sameProcess && (focused || editable)) result.Add(element);
                if (depth < MaxDepth)
                {
                    AutomationElement? first = TreeWalker.RawViewWalker.GetFirstChild(element);
                    if (first != null) pending.Push((first, depth + 1));
                }
            }
            catch (ElementNotAvailableException) { }
            catch { }
        }
        return result;
    }

    private static AutomationElement? FindFocusedCandidate(AutomationElement focused,
        IEnumerable<AutomationElement> candidates, uint processId)
    {
        try
        {
            if (Get(focused, AutomationElement.ProcessIdProperty, 0) == (int)processId &&
                Get(focused, AutomationElement.HasKeyboardFocusProperty, false) &&
                (TryPattern(focused, ValuePattern.Pattern, out ValuePattern? _) ||
                 TryPattern(focused, TextPattern.Pattern, out TextPattern? _)))
                return focused;
        }
        catch { }
        foreach (AutomationElement candidate in candidates)
            if (Get(candidate, AutomationElement.HasKeyboardFocusProperty, false)) return candidate;
        return null;
    }

    private static bool IsConfirmedSearchTextBox(AutomationElement element, uint processId)
    {
        bool hasWritableValue = TryPattern(element, ValuePattern.Pattern, out ValuePattern? value) &&
            value != null && !value.Current.IsReadOnly;
        return IsSearchTextBoxIdentity(
            Get(element, AutomationElement.ProcessIdProperty, 0) == (int)processId,
            Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom) == ControlType.Edit,
            Get(element, AutomationElement.ClassNameProperty, string.Empty),
            Get(element, AutomationElement.AutomationIdProperty, string.Empty),
            Get(element, AutomationElement.FrameworkIdProperty, string.Empty),
            Get(element, AutomationElement.HasKeyboardFocusProperty, false),
            Get(element, AutomationElement.IsKeyboardFocusableProperty, false),
            Get(element, AutomationElement.IsEnabledProperty, false),
            Get(element, AutomationElement.IsOffscreenProperty, true), hasWritableValue);
    }

    internal static bool IsSearchTextBoxIdentity(bool sameProcess, bool isEdit, string className,
        string automationId, string frameworkId, bool hasKeyboardFocus, bool keyboardFocusable,
        bool enabled, bool offscreen, bool writable) =>
        sameProcess && isEdit &&
        string.Equals(className, "RichEditBox", StringComparison.Ordinal) &&
        string.Equals(automationId, "SearchTextBox", StringComparison.Ordinal) &&
        string.Equals(frameworkId, "XAML", StringComparison.Ordinal) &&
        hasKeyboardFocus && keyboardFocusable && enabled && !offscreen && writable;

    internal static bool TryResolveWordRange(string text, int caret, out int start, out int length,
        out string direction, out string reason)
    {
        TextDirection resolvedDirection = DetectTextDirection(text, caret);
        direction = resolvedDirection.ToString();
        return TryResolveWordRange(text, caret, resolvedDirection, out start, out length, out reason);
    }

    private static bool TryResolveWordRange(string text, int caret, TextDirection direction,
        out int start, out int length, out string reason)
    {
        start = length = 0;
        reason = "NoAdjacentWord";
        if (caret < 0 || caret > text.Length)
        {
            reason = "CaretOutOfRange";
            return false;
        }

        int left = caret;
        while (left > 0 && !char.IsWhiteSpace(text[left - 1])) left--;
        int right = caret;
        while (right < text.Length && !char.IsWhiteSpace(text[right])) right++;
        bool touchesLeft = left < caret;
        bool touchesRight = right > caret;
        if (touchesLeft || touchesRight)
        {
            start = left;
            length = right - left;
            reason = touchesLeft && touchesRight ? "CaretInsideWord" :
                touchesLeft ? "LogicalWordBeforeCaret" : "LogicalWordAfterCaret";
            return length > 0;
        }

        int previousEnd = caret;
        while (previousEnd > 0 && char.IsWhiteSpace(text[previousEnd - 1])) previousEnd--;
        int previousStart = previousEnd;
        while (previousStart > 0 && !char.IsWhiteSpace(text[previousStart - 1])) previousStart--;

        int nextStart = caret;
        while (nextStart < text.Length && char.IsWhiteSpace(text[nextStart])) nextStart++;
        int nextEnd = nextStart;
        while (nextEnd < text.Length && !char.IsWhiteSpace(text[nextEnd])) nextEnd++;

        bool hasPrevious = previousStart < previousEnd;
        bool hasNext = nextStart < nextEnd;
        if (direction == TextDirection.RTL && hasNext || !hasPrevious && hasNext)
        {
            start = nextStart;
            length = nextEnd - nextStart;
            reason = "LogicalWordAfterWhitespace";
            return true;
        }
        if (hasPrevious)
        {
            start = previousStart;
            length = previousEnd - previousStart;
            reason = "LogicalWordBeforeWhitespace";
            return true;
        }
        return false;
    }

    private static TextDirection DetectTextDirection(string text, int caret)
    {
        for (int index = Math.Clamp(caret, 0, text.Length); index < text.Length; index++)
            if (TryGetStrongDirection(text[index], out TextDirection direction)) return direction;
        for (int index = Math.Min(caret - 1, text.Length - 1); index >= 0; index--)
            if (TryGetStrongDirection(text[index], out TextDirection direction)) return direction;
        return TextDirection.LTR;
    }

    private static bool TryGetStrongDirection(char character, out TextDirection direction)
    {
        if ((character >= '\u0590' && character <= '\u08FF') ||
            (character >= '\uFB1D' && character <= '\uFEFC'))
        {
            direction = TextDirection.RTL;
            return true;
        }
        if (char.IsLetter(character))
        {
            direction = TextDirection.LTR;
            return true;
        }
        direction = default;
        return false;
    }

    private enum TextDirection { LTR, RTL }

    private static int Offset(TextPattern text, TextPatternRange range, TextPatternRangeEndpoint endpoint)
    {
        TextPatternRange prefix = text.DocumentRange.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, endpoint);
        return prefix.GetText(-1).Length;
    }

    private static string MapPlanFailure(string reason) => reason switch
    {
        "NoFragmentBeforeCaret" => "WindowsSearchWordNotFound",
        "CaretOutOfRange" => "WindowsSearchCaretUnavailable",
        _ => "WindowsSearchReplacementFailed"
    };

    private static string MapReplacementResult(string result) => result switch
    {
        "WindowsSearchSuccess" => "WindowsSearchSuccess",
        "TargetChangedBeforeTargetedReplacement" or "FocusChangedBeforeTargetedReplacement" => "WindowsSearchFocusChanged",
        "FocusChangedAfterTargetedReplacement" => "WindowsSearchFocusChanged",
        "TextChangedBeforeTargetedReplacement" => "WindowsSearchReplacementUnverified",
        "TargetedSelectionVerificationFailed" or "TargetedSelectionTextMismatch" or "TargetedSelectionStartMoveFailed" or "TargetedSelectionEndMoveFailed" => "WindowsSearchSelectionUnverified",
        "TargetedReplacementUnverified" => "WindowsSearchReplacementUnverified",
        "TargetedReplacementSucceededLayoutSwitchFailed" => "WindowsSearchReplacementSucceededLayoutSwitchFailed",
        _ => "WindowsSearchReplacementFailed"
    };

    private static void LogElement(TextReplacementOperation operation, string label, AutomationElement element, int depth)
    {
        Log(operation, $"{label}: depth={depth} ControlType={Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom).ProgrammaticName} ClassName=\"{Escape(Get(element, AutomationElement.ClassNameProperty, string.Empty))}\" AutomationId=\"{Escape(Get(element, AutomationElement.AutomationIdProperty, string.Empty))}\" Name=\"{Escape(Get(element, AutomationElement.NameProperty, string.Empty))}\" FrameworkId=\"{Escape(Get(element, AutomationElement.FrameworkIdProperty, string.Empty))}\" ProcessId={Get(element, AutomationElement.ProcessIdProperty, 0)} NativeWindowHandle=0x{Get(element, AutomationElement.NativeWindowHandleProperty, 0):X} HasKeyboardFocus={Get(element, AutomationElement.HasKeyboardFocusProperty, false)} IsKeyboardFocusable={Get(element, AutomationElement.IsKeyboardFocusableProperty, false)} IsEnabled={Get(element, AutomationElement.IsEnabledProperty, false)} IsOffscreen={Get(element, AutomationElement.IsOffscreenProperty, true)}");
    }

    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T? value) where T : class
    { value = null; try { value = element.GetCurrentPattern(pattern) as T; return value != null; } catch { return false; } }
    private static T Get<T>(AutomationElement element, AutomationProperty property, T fallback)
    { try { object value = element.GetCurrentPropertyValue(property, true); return value is T typed ? typed : fallback; } catch { return fallback; } }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static void LogText(TextReplacementOperation operation, string name, string value) =>
        Log(operation, $"{name}=\"{Escape(value.Length <= MaximumLoggedTextCharacters ? value : value[..MaximumLoggedTextCharacters] + "…")}\" {name}Length={value.Length}");
    private static void Log(TextReplacementOperation operation, string message) => DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

