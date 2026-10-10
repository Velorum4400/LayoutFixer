using System;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

// Microsoft Store is hosted by ApplicationFrameWindow while the focused CoreWindow can be
// owned by the Store process. This probe performs no write until its UIA contract is observed.
internal static class MicrosoftStoreLastWordHandler
{
    internal static bool Probe(TextReplacementOperation operation, IntPtr foregroundWindow, IntPtr focusedWindow,
        uint foregroundProcessId, uint focusedProcessId, out string result)
    {
        result = "MicrosoftStoreDiagnosticsCaptured";
        var total = Stopwatch.StartNew();
        Log(operation, "MICROSOFT STORE LASTWORD UIA PROBE BEGIN");
        try
        {
            if (TextReplacementService.ForegroundWindow != foregroundWindow)
            {
                result = "MicrosoftStoreFocusChanged";
                return false;
            }

            AutomationElement focused = AutomationElement.FocusedElement;
            LogElement(operation, "FocusedElement", focused);
            Log(operation, $"ForegroundWindow=0x{foregroundWindow.ToInt64():X} FocusedWindow=0x{focusedWindow.ToInt64():X} ForegroundProcessId={foregroundProcessId} FocusedWindowProcessId={focusedProcessId}");

            bool hasText = TryPattern(focused, TextPattern.Pattern, out TextPattern? text) && text != null;
            bool hasValue = TryPattern(focused, ValuePattern.Pattern, out ValuePattern? value) && value != null;
            Log(operation, $"TextPatternAvailable={hasText} ValuePatternAvailable={hasValue} ValueIsReadOnly={(hasValue ? value!.Current.IsReadOnly : true)}");
            if (hasValue)
                LogText(operation, "ValuePatternText", value!.Current.Value ?? string.Empty);
            if (!hasText)
            {
                result = "MicrosoftStoreTextPatternUnavailable";
                return false;
            }

            string documentText = text!.DocumentRange.GetText(-1);
            LogText(operation, "DocumentText", documentText);
            TextPatternRange[] ranges = text.GetSelection();
            Log(operation, $"SelectionCount={ranges.Length}");
            if (ranges.Length != 1)
            {
                result = "MicrosoftStoreCaretUnavailable";
                return false;
            }
            int selectionStart = Offset(text, ranges[0], TextPatternRangeEndpoint.Start);
            int selectionEnd = Offset(text, ranges[0], TextPatternRangeEndpoint.End);
            bool caretAvailable = selectionStart == selectionEnd;
            Log(operation, $"SelectionStart={selectionStart} SelectionEnd={selectionEnd} CaretAvailable={caretAvailable}");
            Log(operation, "RangeSelectAvailable=TextPatternRange.Select (not invoked during diagnostics)");
            result = caretAvailable ? "MicrosoftStoreUiaCapabilitiesCaptured" : "MicrosoftStoreCaretUnavailable";
            return false;
        }
        catch (ElementNotAvailableException) { result = "MicrosoftStoreFocusChanged"; return false; }
        catch (Exception ex) { result = "MicrosoftStore" + ex.GetType().Name; return false; }
        finally
        {
            Log(operation, $"MICROSOFT STORE LASTWORD UIA PROBE END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    private static int Offset(TextPattern text, TextPatternRange range, TextPatternRangeEndpoint endpoint)
    {
        TextPatternRange prefix = text.DocumentRange.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, endpoint);
        return prefix.GetText(-1).Length;
    }

    private static bool TryPattern<T>(AutomationElement element, AutomationPattern pattern, out T? value) where T : class
    { value = null; try { value = element.GetCurrentPattern(pattern) as T; return value != null; } catch { return false; } }
    private static T Get<T>(AutomationElement element, AutomationProperty property, T fallback)
    { try { object value = element.GetCurrentPropertyValue(property, true); return value is T typed ? typed : fallback; } catch { return fallback; } }
    private static void LogElement(TextReplacementOperation operation, string name, AutomationElement element) =>
        Log(operation, $"{name} ControlType={Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom).ProgrammaticName} ClassName=\"{Escape(Get(element, AutomationElement.ClassNameProperty, string.Empty))}\" AutomationId=\"{Escape(Get(element, AutomationElement.AutomationIdProperty, string.Empty))}\" Name=\"{Escape(Get(element, AutomationElement.NameProperty, string.Empty))}\" FrameworkId=\"{Escape(Get(element, AutomationElement.FrameworkIdProperty, string.Empty))}\" ProcessId={Get(element, AutomationElement.ProcessIdProperty, 0)} NativeWindowHandle=0x{Get(element, AutomationElement.NativeWindowHandleProperty, 0):X}");
    private static void LogText(TextReplacementOperation operation, string name, string value) =>
        Log(operation, $"{name}Length={value.Length} preview=\"{Escape(value.Length <= 512 ? value : value[..512] + "…")}\"");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static void Log(TextReplacementOperation operation, string message) =>
        DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

