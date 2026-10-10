using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

// Diagnostic-only route. It never writes through UIA or COM; Word replacement is enabled
// only after the observed automation and COM contracts have been reviewed.
internal static class MicrosoftWordLastWordProbe
{
    private const uint CoinitApartmentThreaded = 0x2;

    internal static bool Probe(TextReplacementOperation operation, IntPtr foregroundWindow, IntPtr focusedWindow,
        uint foregroundProcessId, uint focusedProcessId, out string result)
    {
        result = "MicrosoftWordDiagnosticsCaptured";
        Log(operation, "MICROSOFT WORD LASTWORD PROBE BEGIN");
        try
        {
            if (TextReplacementService.ForegroundWindow != foregroundWindow)
            { result = "MicrosoftWordFocusChanged"; return false; }
            Log(operation, $"ForegroundHwnd=0x{foregroundWindow.ToInt64():X} FocusedHwnd=0x{focusedWindow.ToInt64():X} ForegroundProcessId={foregroundProcessId} FocusedProcessId={focusedProcessId}");

            AutomationElement focused = AutomationElement.FocusedElement;
            LogElement(operation, "FocusedElement", focused);
            bool hasText = TryPattern(focused, TextPattern.Pattern, out TextPattern? text) && text != null;
            // The UIAutomationClient reference shipped with this app does not expose
            // TextPattern2. Keep the capability gap explicit in the probe output.
            const bool hasText2 = false;
            bool hasValue = TryPattern(focused, ValuePattern.Pattern, out ValuePattern? value) && value != null;
            Log(operation, $"TextPatternAvailable={hasText} TextPattern2Available={hasText2} ValuePatternAvailable={hasValue} ValueIsReadOnly={(hasValue ? value!.Current.IsReadOnly : true)}");
            if (hasText)
                LogSelection(operation, text!);

            WordComSnapshot com = CaptureComSnapshot();
            Log(operation, $"WordComAvailable={com.Available} WordApplicationHwnd=0x{com.ApplicationHwnd.ToInt64():X} ApplicationMatchesForeground={com.ApplicationHwnd == foregroundWindow}");
            Log(operation, $"ActiveDocumentAvailable={com.ActiveDocumentAvailable} SelectionAvailable={com.SelectionAvailable}");
            if (com.SelectionAvailable)
                Log(operation, $"WordComSelectionStart={com.SelectionStart} WordComSelectionEnd={com.SelectionEnd} CaretAvailable={com.SelectionStart == com.SelectionEnd}");
            Log(operation, $"DocumentReadOnly={com.DocumentReadOnly} TrackChangesEnabled={com.TrackChangesEnabled} WordComReason={com.Reason}");
            return false;
        }
        catch (ElementNotAvailableException) { result = "MicrosoftWordFocusChanged"; return false; }
        catch (Exception ex) { result = "MicrosoftWord" + ex.GetType().Name; return false; }
        finally { Log(operation, $"MICROSOFT WORD LASTWORD PROBE END result={result}"); }
    }

    private static void LogSelection(TextReplacementOperation operation, TextPattern text)
    {
        try
        {
            TextPatternRange[] selection = text.GetSelection();
            if (selection.Length != 1) { Log(operation, $"SelectionCount={selection.Length} CaretAvailable=False"); return; }
            int start = Offset(text, selection[0], TextPatternRangeEndpoint.Start);
            int end = Offset(text, selection[0], TextPatternRangeEndpoint.End);
            Log(operation, $"SelectionCount=1 SelectionStart={start} SelectionEnd={end} CaretAvailable={start == end}");
            // Selection text is bounded; DocumentRange is deliberately not read for large Word documents.
            Log(operation, $"SelectionPreview=\"{Escape(selection[0].GetText(128))}\"");
        }
        catch (Exception ex) { Log(operation, $"SelectionReadFailed={ex.GetType().Name}"); }
    }

    private static WordComSnapshot CaptureComSnapshot()
    {
        WordComSnapshot snapshot = new(false, IntPtr.Zero, false, false, -1, -1, false, false, "NotStarted");
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            int initialized = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
            object? application = null;
            try
            {
                Guid clsid;
                if (CLSIDFromProgID("Word.Application", out clsid) < 0)
                { snapshot = snapshot with { Reason = "WordProgIdUnavailable" }; return; }
                if (GetActiveObject(ref clsid, IntPtr.Zero, out application) < 0 || application == null)
                { snapshot = snapshot with { Reason = "WordNotRunning" }; return; }
                dynamic word = application;
                IntPtr hwnd = (IntPtr)(int)word.Hwnd;
                object? documentObject = word.ActiveDocument;
                bool activeDocument = documentObject != null;
                bool readOnly = false;
                bool trackChanges = false;
                if (documentObject != null)
                {
                    dynamic document = documentObject;
                    readOnly = (bool)document.ReadOnly;
                    trackChanges = (bool)document.TrackRevisions;
                }
                object? selectionObject = word.Selection;
                bool selectionAvailable = selectionObject != null;
                dynamic? selection = selectionObject;
                int start = selection != null ? (int)selection.Start : -1;
                int end = selection != null ? (int)selection.End : -1;
                snapshot = new(true, hwnd, activeDocument, selectionAvailable, start, end, readOnly, trackChanges, "Success");
            }
            catch (Exception ex) { snapshot = snapshot with { Reason = "Com" + ex.GetType().Name }; }
            finally
            {
                if (application != null && Marshal.IsComObject(application)) Marshal.FinalReleaseComObject(application);
                if (initialized >= 0) CoUninitialize();
                completed.Set();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!completed.Wait(TimeSpan.FromMilliseconds(500)))
            return snapshot with { Reason = "ComProbeTimeout" };
        return snapshot;
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
    private static void LogElement(TextReplacementOperation operation, string label, AutomationElement element) =>
        Log(operation, $"{label} ControlType={Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom).ProgrammaticName} ClassName=\"{Escape(Get(element, AutomationElement.ClassNameProperty, string.Empty))}\" FrameworkId=\"{Escape(Get(element, AutomationElement.FrameworkIdProperty, string.Empty))}\" AutomationId=\"{Escape(Get(element, AutomationElement.AutomationIdProperty, string.Empty))}\" Name=\"{Escape(Get(element, AutomationElement.NameProperty, string.Empty))}\" ProcessId={Get(element, AutomationElement.ProcessIdProperty, 0)}");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    private static void Log(TextReplacementOperation operation, string message) => DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");

    private readonly record struct WordComSnapshot(bool Available, IntPtr ApplicationHwnd, bool ActiveDocumentAvailable,
        bool SelectionAvailable, int SelectionStart, int SelectionEnd, bool DocumentReadOnly, bool TrackChangesEnabled, string Reason);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)] private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll")] private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? objectInstance);
}

