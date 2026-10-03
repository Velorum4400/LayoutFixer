
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

internal static class UiaLastWordProbe
{
    private const int PreviewLimit = 200, AncestorLimit = 6;

    public static void Run(TextReplacementOperation operation)
    {
        var total = Stopwatch.StartNew();
        Log(operation, "UIA LASTWORD PROBE BEGIN");
        bool textReadable = false, selectionReadable = false, caretReadable = false, lastWordReadable = false;
        string reason = "";
        try
        {
            var focusedTimer = Stopwatch.StartNew();
            AutomationElement focused = AutomationElement.FocusedElement;
            Log(operation, $"UiaProbe focusedElapsedMs={Ms(focusedTimer)}");
            if (focused is null) { reason = "NoFocusedElement"; return; }

            var propertyTimer = Stopwatch.StartNew();
            bool password = Get(focused, AutomationElement.IsPasswordProperty, false);
            LogElement(operation, "FOCUSED", focused, 0, password);
            Log(operation, $"UiaProbe propertiesElapsedMs={Ms(propertyTimer)}");
            LogAncestors(operation, focused);

            var patternsTimer = Stopwatch.StartNew();
            bool hasValue = TryPattern(focused, ValuePattern.Pattern, out ValuePattern? valuePattern);
            bool hasText = TryPattern(focused, TextPattern.Pattern, out TextPattern? textPattern);
            // The .NET 8 managed UIAutomationClient reference exposes TextPattern but not
            // TextPattern2/LegacyIAccessiblePattern. Keep that limitation explicit in logs.
            bool hasText2 = false;
            bool hasLegacy = false;
            bool hasSelection = TryPattern(focused, SelectionPattern.Pattern, out SelectionPattern? selection);
            bool hasSelectionItem = TryPattern(focused, SelectionItemPattern.Pattern, out SelectionItemPattern? selectionItem);
            Log(operation, $"UiaProbe patterns Value={hasValue} Text={hasText} Text2={hasText2} LegacyIAccessible={hasLegacy} Selection={hasSelection} SelectionItem={hasSelectionItem}");
            Log(operation, $"UiaProbe patternsElapsedMs={Ms(patternsTimer)}");

            if (hasValue && valuePattern != null)
            {
                bool readOnly = valuePattern.Current.IsReadOnly;
                if (password) Log(operation, $"UiaProbe ValuePattern isReadOnly={readOnly} value=Protected");
                else LogText(operation, "UiaProbe ValuePattern isReadOnly=" + readOnly, valuePattern.Current.Value);
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
            if (!password && caretFromSelection != null && hasText && textPattern != null)
            {
                var caretTimer = Stopwatch.StartNew();
                caretReadable = true;
                Log(operation, "UiaProbe caretMethod=TextPattern.GetSelection degenerate=True semanticValidity=LikelyCaret");
                TextPatternRange prefix = textPattern.DocumentRange.Clone();
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caretFromSelection, TextPatternRangeEndpoint.Start);
                beforeCaret = prefix.GetText(-1);
                LogText(operation, "UiaProbe textBeforeCaret", beforeCaret);
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
            if (!textReadable && !hasValue) reason = "FocusedElementDoesNotExposeTextPatterns";
        }
        catch (ElementNotAvailableException) { reason = "ElementNotAvailable"; Log(operation, "UiaProbe failure=ElementNotAvailable"); }
        catch (Exception ex) { reason = ex.GetType().Name; Log(operation, $"UiaProbe failure={ex.GetType().Name} message=\"{Escape(ex.Message)}\""); }
        finally
        {
            Log(operation, "UIA LASTWORD PROBE SUMMARY");
            Log(operation, $"UiaProbe textReadable={textReadable} selectionReadable={selectionReadable} caretReadable={caretReadable} lastWordReadable={lastWordReadable} replacementAttempted=False reason={reason}");
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
}

