using System;
using System.Diagnostics;
using System.Windows.Automation;

namespace LayoutFixer;

// Deliberately narrow fallback for Monaco when UIA exposes only its accessibility notice.
internal static class MonacoKeyboardFallback
{
    internal static bool TryReplace(TextReplacementOperation operation, AutomationElement editor, out string result)
    {
        ClipboardSnapshot snapshot = new(); uint ourSequence = 0; bool replacementStarted = false; result = "MonacoKeyboardSelectionUnverified";
        var total = Stopwatch.StartNew(); Log(operation, "MONACO KEYBOARD FALLBACK BEGIN");
        try
        {
            bool foreground = TextReplacementService.ForegroundWindow == operation.TargetWindow;
            bool editorVerified = MonacoLastWordFallback.IsMonacoEditor(editor);
            Log(operation, $"MonacoDetected={editorVerified} ScreenReaderModeAccessible=False ForegroundVerified={foreground} FocusedEditorVerified={editorVerified}");
            if (!foreground || !editorVerified) { result = "MonacoKeyboardFocusChanged"; return false; }
            if (!ClipboardService.TryCaptureStable(out snapshot, out _, out _)) { result = "MonacoKeyboardClipboardSnapshotFailed"; return false; }
            if (!TryCopySelectedToken(operation, out string source)) return false;
            string converted = LayoutConverter.Convert(source, operation.SourceMap, operation.TargetMap, out _);
            Log(operation, $"CopiedTextLength={source.Length} ConversionSource={operation.SourceLayout.ShortName} ConversionTarget={operation.TargetLayout.ShortName} ConvertedTextLength={converted.Length}");
            if (LastWordLayoutOnlyCompletion.IsUnchanged(source, converted))
            {
                if (!KeyboardInputService.CollapseSelectionToEnd()) { result = "MonacoKeyboardCaretRestoreFailed"; return false; }
                return LastWordLayoutOnlyCompletion.TryComplete(operation, "MonacoKeyboardFallback", out result);
            }
            if (TextReplacementService.ForegroundWindow != operation.TargetWindow || !MonacoLastWordFallback.IsMonacoEditor(AutomationElement.FocusedElement)) { result = "MonacoKeyboardFocusChanged"; return false; }
            if (!ClipboardService.TrySetText(converted, out ourSequence)) { result = "MonacoKeyboardClipboardWriteFailed"; return false; }
            replacementStarted = true;
            if (!KeyboardInputService.Paste()) { result = "MonacoKeyboardReplacementFailed"; return false; }
            Log(operation, "ReplacementAttempted=True");
            Thread.Sleep(20); // Only permits VS Code to consume the paste; verification follows.
            if (!TryCopySelectedToken(operation, out string actual) || !string.Equals(actual, converted, StringComparison.Ordinal)) { result = "MonacoKeyboardReplacementUnverified"; return false; }
            if (!KeyboardInputService.CollapseSelectionToEnd()) { result = "MonacoKeyboardCaretRestoreFailed"; return false; }
            LayoutSwitchVerificationResult switchResult = KeyboardLayoutService.SwitchLayoutAndVerify(operation.TargetWindow, operation.TargetLayout, out _, out _);
            if (switchResult != LayoutSwitchVerificationResult.Success) { result = "MonacoKeyboardReplacementSucceededLayoutSwitchFailed"; return true; }
            result = "MonacoKeyboardLastWordReplaced"; return true;
        }
        finally
        {
            if (!replacementStarted)
                Log(operation, $"Monaco selection cleanup caretRestored={KeyboardInputService.CollapseSelectionToEnd()}");
            if (ourSequence != 0) ClipboardService.RestoreIfUnchanged(snapshot, ourSequence);
            Log(operation, $"MONACO KEYBOARD FALLBACK END result={result} durationMs={total.ElapsedMilliseconds}");
        }
    }

    private static bool TryCopySelectedToken(TextReplacementOperation operation, out string text)
    {
        text = string.Empty;
        if (TextReplacementService.ForegroundWindow != operation.TargetWindow ||
            !KeyboardInputService.SelectPreviousWord(LastWordSearchDirection.Left)) return false;
        Thread.Sleep(15);
        string marker = "LayoutFixer_Monaco_" + Guid.NewGuid().ToString("N");
        if (!ClipboardService.TrySetText(marker, out uint beforeCopy) || !KeyboardInputService.Copy()) return false;
        var timer = Stopwatch.StartNew();
        if (!ClipboardService.WaitForStableCopy(beforeCopy, timer, out ClipboardCopyResult copy)) return false;
        text = copy.Text;
        bool token = text.Length > 0 && !string.Equals(text, marker, StringComparison.Ordinal) &&
            text.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) < 0;
        Log(operation, $"SelectionMethod=CtrlShiftLeft CopyAttempted=True ClipboardUpdated=True CopiedTextLength={text.Length} SelectionVerified={token}");
        return token;
    }

    private static void Log(TextReplacementOperation operation, string message) => DiagnosticLogStore.Write($"operation={operation.Id:N} {message}");
}

