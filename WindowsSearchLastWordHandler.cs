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

    private static void LogElement(TextReplacementOperation operation, string label, AutomationElement element, int depth)
    {
        Log(operation, $"{label}: depth={depth} ControlType={Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom).ProgrammaticName} ClassName=\"{Escape(Get(element, AutomationElement.ClassNameProperty, string.Empty))}\" AutomationId=\"{Escape(Get(element, AutomationElement.AutomationIdProperty, string.Empty))}\" Name=\"{Escape(Get(element, AutomationElement.NameProperty, string.Empty))}\" FrameworkId=\"{Escape(Get(element, AutomationElement.FrameworkIdProperty, string.Empty))}\" ProcessId={Get(element, AutomationElement.ProcessIdProperty, 0)} NativeWindowHandle=0x{Get(element, AutomationElement.NativeWindowHandleProperty, 0):X} HasKeyboardFocus={Get(element, AutomationElement.HasKeyboardFocusProperty, false)} IsKeyboardFocusable={Get(element, AutomationElement.IsKeyboardFocusableProperty, false)} IsEnabled={Get(element, AutomationElement.IsEnabledProperty, false)} IsOffscreen={Get(element, AutomationElement.IsOffscreenProperty, true)}");
    }

    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T? value) where T : class
    { value = null; try { value = element.GetCurrentPattern(pattern) as T; return value != null; } catch { return false; } }
    private static T Get<T>(AutomationElement element, AutomationProperty property, T fallback)
    { try { object value = element.GetCurrentPropertyValue(property, true); return value is T typed ? typed : fallback; } catch { return fallback; } }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static void Log(TextReplacementOperation operation, string message) => DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

