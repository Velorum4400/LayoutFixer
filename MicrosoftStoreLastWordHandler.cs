using System;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

// The Store search box is hosted in a CoreWindow owned by the Store process while
// ApplicationFrameWindow remains foreground. NativeEditLastWordService verifies that
// relationship before this handler is allowed to operate.
internal static class MicrosoftStoreLastWordHandler
{
    internal static bool TryReplace(TextReplacementOperation operation, IntPtr foregroundWindow, IntPtr focusedWindow,
        uint foregroundProcessId, uint focusedProcessId, out string result)
    {
        result = "MicrosoftStoreFailed";
        var total = Stopwatch.StartNew();
        Log(operation, "MICROSOFT STORE LASTWORD BEGIN");
        try
        {
            if (TextReplacementService.ForegroundWindow != foregroundWindow)
            { result = "MicrosoftStoreFocusChanged"; return false; }

            AutomationElement target = AutomationElement.FocusedElement;
            LogElement(operation, "FocusedElement", target);
            Log(operation, $"ForegroundWindow=0x{foregroundWindow.ToInt64():X} FocusedWindow=0x{focusedWindow.ToInt64():X} ForegroundProcessId={foregroundProcessId} FocusedWindowProcessId={focusedProcessId}");
            if (!IsConfirmedStoreSearchBox(target, focusedProcessId))
            { result = "MicrosoftStoreSearchBoxNotConfirmed"; return false; }
            if (!TryPattern(target, TextPattern.Pattern, out TextPattern? text) || text == null)
            { result = "MicrosoftStoreTextPatternUnavailable"; return false; }
            if (!TryPattern(target, ValuePattern.Pattern, out ValuePattern? value) || value == null || value.Current.IsReadOnly)
            { result = "MicrosoftStoreWritableValueUnavailable"; return false; }

            Log(operation, "TextPatternAvailable=True ValuePatternAvailable=True ValueIsReadOnly=False");
            string documentText = text.DocumentRange.GetText(-1);
            string valueText = value.Current.Value ?? string.Empty;
            LogText(operation, "DocumentText", documentText);
            LogText(operation, "ValuePatternText", valueText);
            Log(operation, $"ValueMatchesDocument={string.Equals(documentText, valueText, StringComparison.Ordinal)}");
            // Store autocomplete has not yet been characterized. A mismatch could be an
            // offered suffix, so do not rewrite a range until it is explicitly understood.
            if (!string.Equals(documentText, valueText, StringComparison.Ordinal))
            { result = "MicrosoftStoreValueAndTextPatternMismatch"; return false; }

            TextPatternRange[] ranges = text.GetSelection();
            Log(operation, $"SelectionCount={ranges.Length}");
            if (ranges.Length != 1)
            { result = "MicrosoftStoreCaretUnavailable"; return false; }
            int selectionStart = Offset(text, ranges[0], TextPatternRangeEndpoint.Start);
            int selectionEnd = Offset(text, ranges[0], TextPatternRangeEndpoint.End);
            bool caretAvailable = selectionStart == selectionEnd && selectionStart >= 0 && selectionStart <= documentText.Length;
            Log(operation, $"SelectionStart={selectionStart} SelectionEnd={selectionEnd} CaretOffset={selectionStart} CaretAvailable={caretAvailable}");
            if (!caretAvailable)
            { result = "MicrosoftStoreCaretUnavailable"; return false; }

            if (!WindowsSearchLastWordHandler.TryResolveWordRange(documentText, selectionStart,
                    out int wordStart, out int wordLength, out string direction, out string rangeReason))
            { result = "MicrosoftStoreWordNotFound"; return false; }
            if (wordStart < 0 || wordLength <= 0 || wordStart + wordLength > documentText.Length)
            { result = "MicrosoftStoreWordRangeOutOfBounds"; return false; }
            string word = documentText.Substring(wordStart, wordLength);
            Log(operation, $"TextDirection={direction} WordRange={wordStart}-{wordStart + wordLength} reason={rangeReason}");
            LogText(operation, "WordText", word);

            string converted = LayoutConverter.Convert(word, operation.SourceMap, operation.TargetMap, out int unchanged);
            Log(operation, $"ConversionSource={operation.SourceLayout.ShortName} ConversionTarget={operation.TargetLayout.ShortName} unchanged={unchanged}");
            LogText(operation, "Converted", converted);
            if (LastWordLayoutOnlyCompletion.IsUnchanged(word, converted))
                return LastWordLayoutOnlyCompletion.TryComplete(operation, "MicrosoftStore", out result);

            string expectedDocument = documentText[..wordStart] + converted + documentText[(wordStart + wordLength)..];
            int expectedCaret = wordStart + converted.Length;
            bool replaced = UiaTargetedReplacement.TryReplace(operation, target, text, target.GetRuntimeId(),
                documentText, wordStart, wordLength, word, converted, expectedDocument, expectedCaret,
                "MicrosoftStore", "MicrosoftStoreSuccess", out string replacementResult);
            result = replacementResult == "TargetedReplacementSucceededLayoutSwitchFailed"
                ? "MicrosoftStoreReplacementSucceededLayoutSwitchFailed" : replacementResult;
            Log(operation, $"ReplacementVerified={replaced && result == "MicrosoftStoreSuccess"}");
            if (replaced && result == "MicrosoftStoreSuccess")
            {
                Log(operation, "LayoutSwitchVerified=True");
                LogText(operation, "FinalDocumentText", text.DocumentRange.GetText(-1));
                TextPatternRange[] finalSelection = text.GetSelection();
                int finalCaret = finalSelection.Length == 1 ? Offset(text, finalSelection[0], TextPatternRangeEndpoint.Start) : -1;
                Log(operation, $"FinalCaretOffset={finalCaret}");
            }
            return replaced;
        }
        catch (ElementNotAvailableException) { result = "MicrosoftStoreFocusChanged"; return false; }
        catch (Exception ex) { result = "MicrosoftStore" + ex.GetType().Name; return false; }
        finally
        {
            Log(operation, $"MICROSOFT STORE LASTWORD END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    internal static bool IsStoreSearchBoxIdentity(bool isEdit, string className, string automationId,
        string name, string frameworkId, bool focused, bool enabled, bool writable) =>
        isEdit && string.Equals(className, "TextBox", StringComparison.Ordinal) &&
        string.Equals(automationId, "TextBox", StringComparison.Ordinal) &&
        string.Equals(name, "Search", StringComparison.Ordinal) &&
        string.Equals(frameworkId, "XAML", StringComparison.Ordinal) && focused && enabled && writable;

    private static bool IsConfirmedStoreSearchBox(AutomationElement element, uint focusedProcessId)
    {
        bool writable = TryPattern(element, ValuePattern.Pattern, out ValuePattern? value) && value != null && !value.Current.IsReadOnly;
        return Get(element, AutomationElement.ProcessIdProperty, 0) == (int)focusedProcessId &&
            IsStoreSearchBoxIdentity(
                Get(element, AutomationElement.ControlTypeProperty, ControlType.Custom) == ControlType.Edit,
                Get(element, AutomationElement.ClassNameProperty, string.Empty),
                Get(element, AutomationElement.AutomationIdProperty, string.Empty),
                Get(element, AutomationElement.NameProperty, string.Empty),
                Get(element, AutomationElement.FrameworkIdProperty, string.Empty),
                Get(element, AutomationElement.HasKeyboardFocusProperty, false),
                Get(element, AutomationElement.IsEnabledProperty, false), writable);
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

