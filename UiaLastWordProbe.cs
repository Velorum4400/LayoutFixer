using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

internal static class UiaLastWordProbe
{
    private const int PreviewLimit = 200, AncestorLimit = 6, MaxTraversalDepth = 8,
        MaxTraversalElements = 240, TraversalBudgetMilliseconds = 800;

    public static bool Run(TextReplacementOperation operation, out string result)
    {
        result = "Aborted";
        var total = Stopwatch.StartNew();
        Log(operation, "UIA LASTWORD PROBE BEGIN");
        bool textReadable = false, selectionReadable = false, caretReadable = false, lastWordReadable = false;
        string reason = "";
        try
        {
            var focusedTimer = Stopwatch.StartNew();
            AutomationElement focused = AutomationElement.FocusedElement;
            Log(operation, $"UiaProbe focusedElapsedMs={Ms(focusedTimer)}");
            if (focused is null) { reason = "NoFocusedElement"; result = reason; return false; }

            var propertyTimer = Stopwatch.StartNew();
            bool password = Get(focused, AutomationElement.IsPasswordProperty, false);
            LogElement(operation, "ROOT", focused, 0, password);
            Log(operation, $"UiaProbe propertiesElapsedMs={Ms(propertyTimer)}");
            LogAncestors(operation, focused);

            bool focusedSuitable = IsSuitableFocusedEdit(focused, password);
            TraversalResult traversal;
            AutomationElement probeElement;
            if (focusedSuitable)
            {
                probeElement = focused;
                traversal = TraversalResult.FocusedElement;
                Log(operation, "UiaLastWord selectedSource=FocusedElement descendantTraversal=False");
            }
            else
            {
                var traversalTimer = Stopwatch.StartNew();
                traversal = FindEditableDescendant(operation, focused);
                probeElement = traversal.Selected ?? focused;
                Log(operation, $"UiaLastWord selectedSource=DescendantTraversal descendantTraversal=True elapsedMs={Ms(traversalTimer)}");
            }
            Log(operation, $"UiaProbe traversal END visitedElements={traversal.Visited} interestingElements={traversal.Interesting} editCandidates={traversal.EditCandidates} focusedDescendantFound={traversal.FocusedDescendantFound} truncated={traversal.Truncated} reason={traversal.StopReason}");
            if (focusedSuitable || traversal.Selected != null)
            {
                Log(operation, $"UiaProbe SELECTED_EDIT reason=\"{traversal.SelectionReason}\"");
                password = Get(probeElement, AutomationElement.IsPasswordProperty, false);
                LogElement(operation, "SELECTED_EDIT", probeElement, traversal.SelectedDepth, password);
            }
            else Log(operation, "UiaProbe selectedEdit=False reason=NoEditableDescendantFound");

            var patternsTimer = Stopwatch.StartNew();
            bool hasValue = TryPattern(probeElement, ValuePattern.Pattern, out ValuePattern? valuePattern);
            bool hasText = TryPattern(probeElement, TextPattern.Pattern, out TextPattern? textPattern);
            // The .NET 8 managed UIAutomationClient reference exposes TextPattern but not
            // TextPattern2/LegacyIAccessiblePattern. Keep that limitation explicit in logs.
            bool hasText2 = false;
            bool hasLegacy = false;
            bool hasSelection = TryPattern(probeElement, SelectionPattern.Pattern, out SelectionPattern? selection);
            bool hasSelectionItem = TryPattern(probeElement, SelectionItemPattern.Pattern, out SelectionItemPattern? selectionItem);
            Log(operation, $"UiaProbe patterns Value={hasValue} Text={hasText} Text2={hasText2} LegacyIAccessible={hasLegacy} Selection={hasSelection} SelectionItem={hasSelectionItem}");
            Log(operation, $"UiaProbe patternsElapsedMs={Ms(patternsTimer)}");

            string? valueText = null;
            if (hasValue && valuePattern != null)
            {
                bool readOnly = valuePattern.Current.IsReadOnly;
                if (password) Log(operation, $"UiaProbe ValuePattern isReadOnly={readOnly} value=Protected");
                else { valueText = valuePattern.Current.Value; LogText(operation, "UiaProbe ValuePattern isReadOnly=" + readOnly, valueText); }
            }

            string? documentText = null;
            TextPatternRange? caretFromSelection = null;
            if (hasText && textPattern != null && !password)
            {
                var textTimer = Stopwatch.StartNew();
                TextPatternRange document = textPattern.DocumentRange;
                documentText = document.GetText(-1);
                textReadable = true;
                LogText(operation, "UiaProbe TextPattern document", documentText);
                TextPatternRange[] ranges = textPattern.GetSelection();
                selectionReadable = true;
                Log(operation, $"UiaProbe TextPattern selectionCount={ranges.Length}");
                for (int i = 0; i < Math.Min(ranges.Length, 3); i++)
                {
                    LogText(operation, $"UiaProbe TextPattern selection[{i}]", ranges[i].GetText(-1));
                    if (i == 0 && ranges[i].CompareEndpoints(TextPatternRangeEndpoint.Start, ranges[i], TextPatternRangeEndpoint.End) == 0)
                        caretFromSelection = ranges[i];
                }
                Log(operation, $"UiaProbe textElapsedMs={Ms(textTimer)}");
            }

            string? beforeCaret = null;
            int caretOffset = -1;
            if (!password && caretFromSelection != null && hasText && textPattern != null)
            {
                var caretTimer = Stopwatch.StartNew();
                caretReadable = true;
                Log(operation, "UiaProbe caretMethod=TextPattern.GetSelection degenerate=True semanticValidity=LikelyCaret");
                TextPatternRange prefix = textPattern.DocumentRange.Clone();
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caretFromSelection, TextPatternRangeEndpoint.Start);
                beforeCaret = prefix.GetText(-1);
                caretOffset = beforeCaret.Length;
                LogText(operation, "UiaProbe textBeforeCaret", beforeCaret);
                Log(operation, $"UiaProbe caretOffset={caretOffset} fullTextLength={documentText?.Length ?? -1}");
                Log(operation, $"UiaProbe caretElapsedMs={Ms(caretTimer)}");
            }

            string source = beforeCaret ?? documentText ?? string.Empty;
            if (!password && source.Length > 0)
            {
                var wordTimer = Stopwatch.StartNew();
                LastWordSelectionAnalysis analysis = LastWordSelectionAnalyzer.Analyze(source);
                if (analysis.HasFragment)
                {
                    string fragment = source.Substring(analysis.FragmentStart, analysis.FragmentLength);
                    string converted = LayoutConverter.Convert(fragment, operation.SourceMap, operation.TargetMap, out _);
                    lastWordReadable = true;
                    Log(operation, $"UiaProbe lastWordRange start={analysis.FragmentStart} end={analysis.FragmentStart + analysis.FragmentLength}");
                    LogText(operation, "UiaProbe fragment", fragment);
                    LogText(operation, "UiaProbe converted", converted);
                }
                Log(operation, $"UiaProbe lastWordElapsedMs={Ms(wordTimer)}");
            }
            if (!textReadable && !hasValue) { reason = traversal.Selected == null ? "NoEditableDescendantFound" : "SelectedElementDoesNotExposeTextPatterns"; result = reason; return false; }
            if (password) { reason = "PasswordField"; result = reason; return false; }
            if (valueText == null || documentText == null || !string.Equals(valueText, documentText, StringComparison.Ordinal))
            { reason = "ValueAndTextPatternMismatch"; Log(operation, $"UiaWholeValue textsEquivalent=False valueTextLength={valueText?.Length ?? -1} textPatternLength={documentText?.Length ?? -1}"); result = reason; return false; }
            Log(operation, $"UiaWholeValue textsEquivalent=True valueTextLength={valueText.Length} textPatternLength={documentText.Length}");
            if (valueText.Length > UiaWriteProbe.MaximumValueLength) { reason = "ValueTooLargeForExperimentalWholeValueReplacement"; result = reason; return false; }
            if (caretFromSelection == null || caretOffset < 0) { reason = "UnsupportedSelection"; result = reason; return false; }
            if (!UiaWriteProbe.TryBuildReplacement(valueText, caretOffset,
                    LayoutConverter.Convert(valueText.Substring(LastWordSelectionAnalyzer.Analyze(valueText[..caretOffset]).FragmentStart, LastWordSelectionAnalyzer.Analyze(valueText[..caretOffset]).FragmentLength), operation.SourceMap, operation.TargetMap, out _),
                    out int start, out int end, out string plannedFragment, out string replacement, out int expectedCaret, out reason))
            { result = reason; return false; }
            Log(operation, $"UiaWholeValue caretOffset={caretOffset} lastWordStart={start} lastWordEnd={end} oldLength={valueText.Length} newLength={replacement.Length} expectedCaretOffset={expectedCaret}");
            LogText(operation, "UiaWholeValue fragment", plannedFragment);
            string plannedConverted = replacement.Substring(start, expectedCaret - start);
            LogText(operation, "UiaWholeValue converted", plannedConverted);
            bool success = UiaWriteProbe.TryReplace(operation, probeElement, probeElement.GetRuntimeId(), valueText, replacement, expectedCaret, out result);
            reason = result;
            return success;
        }
        catch (ElementNotAvailableException) { reason = "ElementNotAvailable"; result = reason; Log(operation, "UiaProbe failure=ElementNotAvailable"); return false; }
        catch (Exception ex) { reason = ex.GetType().Name; result = reason; Log(operation, $"UiaProbe failure={ex.GetType().Name} message=\"{Escape(ex.Message)}\""); return false; }
        finally
        {
            Log(operation, "UIA LASTWORD PROBE SUMMARY");
            Log(operation, $"UiaProbe textReadable={textReadable} selectionReadable={selectionReadable} caretReadable={caretReadable} lastWordReadable={lastWordReadable} result={result} reason={reason}");
            Log(operation, $"UiaProbe PERF total={Ms(total)}");
            Log(operation, $"UIA LASTWORD PROBE END durationMs={Ms(total)}");
        }
    }

    private static void LogAncestors(TextReplacementOperation operation, AutomationElement current)
    {
        TreeWalker walker = TreeWalker.ControlViewWalker;
        for (int depth = 1; depth <= AncestorLimit; depth++)
        {
            current = walker.GetParent(current);
            if (current is null) break;
            LogElement(operation, "ANCESTOR", current, depth, Get(current, AutomationElement.IsPasswordProperty, false));
        }
    }

    internal static bool IsFocusedEditCandidate(bool isEdit, bool hasKeyboardFocus, bool enabled,
        bool password, bool hasValuePattern, bool hasTextPattern) =>
        isEdit && hasKeyboardFocus && enabled && !password && (hasValuePattern || hasTextPattern);

    private static bool IsSuitableFocusedEdit(AutomationElement element, bool password) =>
        IsFocusedEditCandidate(
            Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom) == ControlType.Edit,
            Get(element, AutomationElement.HasKeyboardFocusProperty, false),
            Get(element, AutomationElement.IsEnabledProperty, false), password,
            TryPattern(element, ValuePattern.Pattern, out ValuePattern? _),
            TryPattern(element, TextPattern.Pattern, out TextPattern? _));

    private static TraversalResult FindEditableDescendant(TextReplacementOperation operation, AutomationElement root)
    {
        Log(operation, $"UiaProbe traversal BEGIN maxDepth={MaxTraversalDepth} maxElements={MaxTraversalElements} timeBudgetMs={TraversalBudgetMilliseconds}");
        var stopwatch = Stopwatch.StartNew();
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        TreeWalker walker = TreeWalker.RawViewWalker;
        try
        {
            AutomationElement? child = walker.GetFirstChild(root);
            if (child != null) pending.Push((child, 1));
        }
        catch { return new TraversalResult(0, 0, 0, false, false, "RootChildrenUnavailable", null, 0, ""); }

        int visited = 0, interesting = 0, edits = 0;
        bool focusedFound = false;
        Candidate? best = null;
        string stopReason = "Completed";
        while (pending.Count > 0)
        {
            if (visited >= MaxTraversalElements) { stopReason = "MaxElements"; break; }
            if (stopwatch.ElapsedMilliseconds >= TraversalBudgetMilliseconds) { stopReason = "TimeBudget"; break; }
            (AutomationElement element, int depth) = pending.Pop();
            visited++;
            try
            {
                AutomationElement? sibling = walker.GetNextSibling(element);
                if (sibling != null) pending.Push((sibling, depth));
                bool focus = Get(element, AutomationElement.HasKeyboardFocusProperty, false);
                bool focusable = Get(element, AutomationElement.IsKeyboardFocusableProperty, false);
                bool enabled = Get(element, AutomationElement.IsEnabledProperty, false);
                bool offscreen = Get(element, AutomationElement.IsOffscreenProperty, true);
                ControlType type = Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom);
                bool edit = type == ControlType.Edit;
                bool value = TryPattern(element, ValuePattern.Pattern, out ValuePattern? _);
                bool text = TryPattern(element, TextPattern.Pattern, out TextPattern? _);
                bool isInteresting = edit || focus || focusable || value || text;
                if (isInteresting)
                {
                    interesting++;
                    if (edit) edits++;
                    int score = (focus ? 100 : 0) + (edit ? 40 : 0) + (focusable ? 20 : 0) +
                        (enabled && !offscreen ? 10 : 0) + (text ? 8 : 0) + (value ? 5 : 0);
                    Log(operation, $"UiaProbe DESCENDANT depth={depth} score={score} edit={edit} focus={focus} focusable={focusable} value={value} text={text}");
                    LogElement(operation, edit ? "EDIT_CANDIDATE" : "INTERESTING", element, depth,
                        Get(element, AutomationElement.IsPasswordProperty, false));
                    if (focus) focusedFound = true;
                    if (edit || focus || value || text)
                    {
                        var candidate = new Candidate(element, depth, score, focus, edit, text, value);
                        if (best == null || candidate.Score > best.Score) best = candidate;
                    }
                }
                if (depth < MaxTraversalDepth)
                {
                    AutomationElement? firstChild = walker.GetFirstChild(element);
                    if (firstChild != null) pending.Push((firstChild, depth + 1));
                }
            }
            catch (ElementNotAvailableException) { }
            catch { }
        }
        bool truncated = stopReason != "Completed";
        string selectionReason = best == null ? "" : best.Focused ? "HasKeyboardFocus=True" :
            best.Edit ? "BestEditCandidate" : best.Text ? "BestTextPatternCandidate" : "BestValuePatternCandidate";
        return new TraversalResult(visited, interesting, edits, focusedFound, truncated, stopReason,
            best?.Element, best?.Depth ?? 0, selectionReason);
    }

    private static void LogElement(TextReplacementOperation operation, string kind, AutomationElement element, int depth, bool password)
    {
        Log(operation, $"UiaProbe {kind} depth={depth} controlType=\"{Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom).ProgrammaticName}\" name=\"{Escape(Get(element, AutomationElement.NameProperty, string.Empty))}\" automationId=\"{Escape(Get(element, AutomationElement.AutomationIdProperty, string.Empty))}\" className=\"{Escape(Get(element, AutomationElement.ClassNameProperty, string.Empty))}\" frameworkId=\"{Escape(Get(element, AutomationElement.FrameworkIdProperty, string.Empty))}\" processId={Get(element, AutomationElement.ProcessIdProperty, 0)} nativeHwnd=0x{Get(element, AutomationElement.NativeWindowHandleProperty, 0):X} hasKeyboardFocus={Get(element, AutomationElement.HasKeyboardFocusProperty, false)} keyboardFocusable={Get(element, AutomationElement.IsKeyboardFocusableProperty, false)} enabled={Get(element, AutomationElement.IsEnabledProperty, false)} offscreen={Get(element, AutomationElement.IsOffscreenProperty, false)} password={password} bounds={FormatRect(Get(element, AutomationElement.BoundingRectangleProperty, Rect.Empty))}");
    }

    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T? value) where T : class
    { value = null; try { value = element.GetCurrentPattern(pattern) as T; return value != null; } catch { return false; } }
    private static T Get<T>(AutomationElement e, AutomationProperty p, T fallback) { try { object v = e.GetCurrentPropertyValue(p, true); return v is T value ? value : fallback; } catch { return fallback; } }
    private static void LogText(TextReplacementOperation o, string prefix, string value) => Log(o, $"{prefix}Length={value.Length} preview=\"{Escape(value.Length <= PreviewLimit ? value : value[..PreviewLimit] + "…")}\" truncated={value.Length > PreviewLimit}");
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static string FormatRect(Rect r) => r.IsEmpty ? "empty" : $"{r.Left:F0},{r.Top:F0},{r.Right:F0},{r.Bottom:F0}";
    private static string Ms(Stopwatch s) => (s.ElapsedTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms";
    private static void Log(TextReplacementOperation o, string m) => DiagnosticLogStore.Write($"operation={o.Id:N} {m}");

    private sealed record Candidate(AutomationElement Element, int Depth, int Score, bool Focused,
        bool Edit, bool Text, bool Value);
    private sealed record TraversalResult(int Visited, int Interesting, int EditCandidates,
        bool FocusedDescendantFound, bool Truncated, string StopReason, AutomationElement? Selected,
        int SelectedDepth, string SelectionReason)
    {
        public static TraversalResult FocusedElement { get; } = new(0, 0, 1, true, false,
            "FocusedElementSelected", null, 0, "FocusedElement");
    }
}

