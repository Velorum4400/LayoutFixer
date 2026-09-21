using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace LayoutFixer;

public static class TextFixer
{
    internal static IntPtr ForegroundWindow => GetForegroundWindow();
    private const int VK_CONTROL = 0x11;
    private const int VK_A = 0x41;
    private const int VK_C = 0x43;
    private const int VK_V = 0x56;
    private const int VK_SHIFT = 0x10;
    private const int VK_LEFT = 0x25;
    private const int VK_BACK = 0x08;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint WM_COPY = 0x0301;
    private const uint INPUT_KEYBOARD = 1;

    public static bool TryFix(
        bool lastWord,
        SelectionPreserver.SelectionSnapshot? selectionSnapshot,
        out KeyboardLanguage from,
        out KeyboardLanguage to,
        IntPtr expectedWindow = default,
        bool captureSelection = false)
    {
        IntPtr targetWindow = GetForegroundWindow();
        IntPtr focusWindow = GetFocusedWindow(targetWindow);
        KeyboardLanguage? currentLayout = GetCurrentLanguage(targetWindow);

        from = currentLayout ?? KeyboardLanguage.English;
        to = from;
        if (expectedWindow != IntPtr.Zero && targetWindow != expectedWindow)
        {
            Log("FAIL: focus changed before correction started");
            return false;
        }
        var elapsed = Stopwatch.StartNew();

        string available = string.Join(",", KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.ShortName));
        Log($"========== START lastWord={lastWord}, target=0x{targetWindow.ToInt64():X}, focus=0x{focusWindow.ToInt64():X}, currentLayout={(currentLayout?.ToString() ?? "Unsupported")}, available=[{available}]");

        ClipboardSnapshot clipboardSnapshot = CaptureClipboardSnapshot();
        bool clipboardChanged = false;
        Log($"TIMING clipboard snapshot: {elapsed.ElapsedMilliseconds} ms");

        try
        {
            if (captureSelection)
                selectionSnapshot ??= SelectionPreserver.CaptureSelection();
            Log($"TIMING selection snapshot: {elapsed.ElapsedMilliseconds} ms");
            if (!CorrectionWorker.CanContinue || GetForegroundWindow() != targetWindow || GetFocusedWindow(targetWindow) != focusWindow)
            {
                Log("FAIL: focus changed while preparing correction");
                return false;
            }
            string? original;
            LastWordTarget? lastWordTarget = null;

            if (lastWord)
            {
                bool nativeHandled = NativeEditSelection.TrySelect(
                    focusWindow, out original, out string nativeDiagnostic);
                Log(nativeDiagnostic);
                if (nativeHandled && string.IsNullOrEmpty(original))
                {
                    Log("FAIL: native editor has no confirmed word selection");
                    return false;
                }
                if (!nativeHandled)
                    original = TryGetSelectionOrSelectLastWordViaAutomation(
                        focusWindow != IntPtr.Zero ? focusWindow : targetWindow,
                        out lastWordTarget);
            }
            else
            {
                SelectAll();
                Log("Ctrl+A sent");
                Thread.Sleep(180);
                original = TryGetTextViaAutomation(
                    focusWindow != IntPtr.Zero ? focusWindow : targetWindow,
                    selectionOnly: false);
            }

            if (!CorrectionWorker.CanContinue || GetForegroundWindow() != targetWindow || GetFocusedWindow(targetWindow) != focusWindow)
            {
                Log("FAIL: focus changed while reading text");
                return false;
            }

            if (string.IsNullOrEmpty(original))
            {
                // Some Chromium/WebView editors (including the ChatGPT Windows app)
                // expose text through the clipboard but do not expose a usable UIA
                // caret/range. In last-word mode, create the selection with normal
                // keyboard input before copying. This makes Ctrl+V replace the word
                // instead of appending the converted text after it.
                if (lastWord)
                {
                    TryClearClipboard();
                    clipboardChanged = true;
                    Log("Clipboard clear attempted for keyboard fallback");
                    Log("UIA last-word selection unavailable; trying Ctrl+Shift+Left keyboard fallback");
                    SelectPreviousWordWithKeyboard();
                    Thread.Sleep(40);
                }

                uint seqCopyStart = GetClipboardSequenceNumber();
                Copy();
                original = WaitForClipboardText(seqCopyStart, 12);

                if (string.IsNullOrEmpty(original) && focusWindow != IntPtr.Zero)
                {
                    SendMessage(focusWindow, WM_COPY, IntPtr.Zero, IntPtr.Zero);
                    original = WaitForClipboardText(GetClipboardSequenceNumber(), 20, allowSameSequence: true);
                }
            }

            if (string.IsNullOrEmpty(original))
            {
                Log("FAIL: no text obtained");
                return false;
            }

            Log($"Clipboard text obtained, length={original.Length}, sample={Sample(original)}");
            Log($"TIMING text acquired: {elapsed.ElapsedMilliseconds} ms");

            KeyboardLanguage fallback = currentLayout ?? KeyboardLanguage.English;
            from = LayoutConverter.DetectLanguage(original, fallback);

            if (currentLayout.HasValue &&
                currentLayout.Value != from &&
                KeyboardLayout.IsAvailable(currentLayout.Value))
            {
                to = currentLayout.Value;
            }
            else if (!KeyboardLayout.TryGetNext(from, out to))
            {
                Log($"FAIL: fewer than two supported installed layouts; source={from}");
                return false;
            }

            if (!KeyboardLayout.IsAvailable(to))
            {
                Log($"FAIL: target layout is not installed: {to}");
                return false;
            }

            Log($"Detected direction: text={from}, currentLayout={(currentLayout?.ToString() ?? "Unsupported")}, target={to}");

            string converted = LayoutConverter.Convert(original, from, to);
            if (converted == original)
            {
                Log($"FAIL: converted text is identical to original; direction={from}->{to}");
                return false;
            }

            Log($"Converted sample={Sample(converted)}");

            // Chromium and Qt can report the complete UIA range while their
            // visual selection contains only a bidi run. Use the saved logical
            // caret, delete the known word and send Unicode directly.
            if (lastWordTarget != null)
            {
                if (!DeleteLastWord(lastWordTarget, original, targetWindow))
                    return false;
                if (!CorrectionWorker.CanContinue || GetForegroundWindow() != targetWindow || GetFocusedWindow(targetWindow) != focusWindow)
                {
                    Log("FAIL: focus changed before direct Unicode replacement");
                    return false;
                }
                SendUnicodeText(converted);
                Log($"Last-word replacement sent as direct Unicode text, length={converted.Length}");
                Log($"TIMING direct replacement: {elapsed.ElapsedMilliseconds} ms");
                SwitchForegroundLayout(targetWindow, focusWindow, to);
                Log("Layout switch request sent");
                Log("SUCCESS");
                return true;
            }

            if (!CorrectionWorker.CanContinue || GetForegroundWindow() != targetWindow || GetFocusedWindow(targetWindow) != focusWindow)
            {
                Log("FAIL: focus changed before replacement");
                return false;
            }

            Log($"TIMING clipboard publish start: {elapsed.ElapsedMilliseconds} ms");
            if (!SetClipboardTextWithRetry(converted))
            {
                Log("FAIL: could not write converted text to clipboard");
                return false;
            }
            clipboardChanged = true;

            Log("Converted text placed into clipboard");
            Log($"TIMING clipboard publish end: {elapsed.ElapsedMilliseconds} ms");

            if (!CorrectionWorker.CanContinue || GetForegroundWindow() != targetWindow || GetFocusedWindow(targetWindow) != focusWindow)
            {
                Log("FAIL: focus changed before paste");
                return false;
            }

            if (!Paste())
            {
                Log("FAIL: SendInput paste returned false");
                return false;
            }

            Log("Ctrl+V sent");

            // Wait until the target control has actually consumed Ctrl+V before
            // restoring the user's original clipboard. For partial corrections,
            // verify the text immediately before the post-paste caret; this also
            // works when LayoutFixer selected the last word itself and there was
            // no pre-existing SelectionSnapshot.
            bool pasteConfirmed = WaitForPasteCompletion(
                focusWindow != IntPtr.Zero ? focusWindow : targetWindow,
                converted,
                lastWord);

            if (!pasteConfirmed)
            {
                // UI Automation is not reliable in every editor. Keep the
                // converted clipboard available for a conservative grace period
                // instead of restoring the old clipboard asynchronously.
                Log("Paste not confirmed through UI Automation; waiting before clipboard restore");
                Thread.Sleep(180);
            }

            if (selectionSnapshot?.HasSelection == true)
            {
                bool restored = SelectionPreserver.RestoreSelection(selectionSnapshot);
                Log($"Selection restore result={restored}");
            }

            SwitchForegroundLayout(targetWindow, focusWindow, to);
            Log("Layout switch request sent");
            Log("SUCCESS");
            return true;
        }
        catch (Exception ex)
        {
            Log("EXCEPTION: " + ex);
            return false;
        }
        finally
        {
            if (clipboardChanged)
                RestoreClipboardSnapshot(clipboardSnapshot);
            Log($"TIMING total: {elapsed.ElapsedMilliseconds} ms");
            Log("========== END ==========" + Environment.NewLine);
        }
    }

    private sealed class ClipboardSnapshot
    {
        public DataObject? Data { get; init; }
        public bool Captured { get; init; }
        public bool HasData { get; init; }
        public int FormatCount { get; init; }
    }

    private static ClipboardSnapshot CaptureClipboardSnapshot()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                IDataObject? source = Clipboard.GetDataObject();
                if (source == null)
                    return new ClipboardSnapshot { Captured = true };

                string[] formats = source.GetFormats(autoConvert: false);
                var copy = new DataObject();
                int copied = 0;

                foreach (string format in formats)
                {
                    try
                    {
                        object? value = source.GetData(format, autoConvert: false);
                        if (value == null)
                            continue;

                        if (value is Stream stream)
                        {
                            long oldPosition = 0;
                            if (stream.CanSeek)
                            {
                                oldPosition = stream.Position;
                                stream.Position = 0;
                            }

                            var clone = new MemoryStream();
                            stream.CopyTo(clone);
                            clone.Position = 0;

                            if (stream.CanSeek)
                                stream.Position = oldPosition;

                            value = clone;
                        }

                        copy.SetData(format, autoConvert: false, value);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        Log($"Clipboard snapshot skipped format '{format}': {ex.GetType().Name}");
                    }
                }

                return new ClipboardSnapshot
                {
                    Data = copied > 0 ? copy : null,
                    Captured = true,
                    HasData = copied > 0,
                    FormatCount = copied
                };
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
        }

        return new ClipboardSnapshot();
    }

    private static void RestoreClipboardSnapshot(ClipboardSnapshot snapshot)
    {
        if (!snapshot.Captured)
            return;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if (snapshot.HasData && snapshot.Data != null)
                    Clipboard.SetDataObject(snapshot.Data, copy: true);
                else
                    Clipboard.Clear();

                Log($"Clipboard restored, formats={snapshot.FormatCount}");
                return;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Log($"Clipboard restore failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }
    }

    private static bool WaitForPasteCompletion(IntPtr hwnd, string converted, bool partialCorrection)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            try
            {
                AutomationElement? element = AutomationElement.FocusedElement;
                if (element == null && hwnd != IntPtr.Zero)
                    element = AutomationElement.FromHandle(hwnd);

                if (element != null &&
                    element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                {
                    var textPattern = (TextPattern)patternObj;
                    TextPatternRange[] ranges = textPattern.GetSelection();

                    if (partialCorrection && ranges != null && ranges.Length > 0)
                    {
                        TextPatternRange current = ranges[0];
                        string selected = current.GetText(-1);

                        // Some editors keep the newly pasted text selected.
                        if (string.Equals(selected, converted, StringComparison.Ordinal))
                        {
                            Log($"Paste completion confirmed by selection on attempt {attempt + 1}");
                            return true;
                        }

                        // Most editors collapse the selection at the end of the
                        // inserted text. Rebuild a range directly before that caret.
                        if (string.IsNullOrEmpty(selected) &&
                            TextBeforeCaretEquals(current, converted))
                        {
                            Log($"Paste completion confirmed before caret on attempt {attempt + 1}");
                            return true;
                        }
                    }

                    // Do not retrieve an entire WebView document on each partial
                    // paste poll; only the selection/caret can confirm this case.
                    if (!partialCorrection)
                    {
                        string documentText = textPattern.DocumentRange.GetText(-1);
                        if (string.Equals(documentText.TrimEnd('\r', '\n'),
                            converted.TrimEnd('\r', '\n'), StringComparison.Ordinal))
                        {
                            Log($"Paste completion confirmed for full text on attempt {attempt + 1}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Paste completion UIA check failed: {ex.GetType().Name}: {ex.Message}");
                break;
            }

            Thread.Sleep(8);
        }

        return false;
    }

    private static bool TextBeforeCaretEquals(TextPatternRange caretRange, string expected)
    {
        if (expected.Length == 0)
            return true;

        TextPatternRange range = caretRange.Clone();
        range.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            range,
            TextPatternRangeEndpoint.Start);

        int moved = range.MoveEndpointByUnit(
            TextPatternRangeEndpoint.Start,
            TextUnit.Character,
            -expected.Length);

        if (Math.Abs(moved) != expected.Length)
            return false;

        return string.Equals(range.GetText(-1), expected, StringComparison.Ordinal);
    }

    private static KeyboardLanguage? GetCurrentLanguage(IntPtr hwnd)
    {
        uint threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        IntPtr hkl = GetKeyboardLayout(threadId);

        return KeyboardLayout.TryGetLanguage(hkl, out KeyboardLanguage language)
            ? language
            : null;
    }

    private static void SwitchForegroundLayout(IntPtr hwnd, IntPtr focusHwnd, KeyboardLanguage language)
    {
        if (!KeyboardLayout.TryGetInstalledHkl(language, out IntPtr hkl))
            return;

        if (focusHwnd != IntPtr.Zero)
            PostMessage(focusHwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);

        if (hwnd != IntPtr.Zero && hwnd != focusHwnd)
            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }

    private static void TryClearClipboard()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Clipboard.Clear();
                return;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }
    }

    private static string? WaitForClipboardText(uint initialSequence, int attempts, bool allowSameSequence = false)
    {
        // Preserve the old overall timeout for slow targets, but react quickly
        // when clipboard data arrives instead of sleeping in 75 ms increments.
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < attempts * 75)
        {
            try
            {
                uint seq = GetClipboardSequenceNumber();
                bool changed = seq != initialSequence;

                if ((changed || allowSameSequence) && Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    string value = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch (ExternalException) { }

            Thread.Sleep(10);
        }

        return null;
    }

    private static bool SetClipboardTextWithRetry(string text) => NativeClipboard.TrySetText(text);

    private static IntPtr GetFocusedWindow(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
            return IntPtr.Zero;

        uint targetThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };

        return GetGUIThreadInfo(targetThread, ref info)
            ? (info.hwndFocus != IntPtr.Zero ? info.hwndFocus : foreground)
            : foreground;
    }

    private static string Sample(string value)
    {
        string s = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length <= 80 ? s : s[..80] + "...";
    }

    private static void Log(string message) => DiagnosticLogStore.Write(false, message);

    private sealed record LastWordTarget(AutomationElement Element, TextPattern Pattern, TextPatternRange End);

    private static bool SelectionCopiesExactly(string expected)
    {
        TryClearClipboard();
        uint sequence = GetClipboardSequenceNumber();
        Copy();
        string? copied = WaitForClipboardText(sequence, 4);
        bool confirmed = string.Equals(copied, expected, StringComparison.Ordinal);
        Log(confirmed
            ? "Last-word selection confirmed through clipboard"
            : $"Last-word selection unreliable, copiedLength={copied?.Length ?? 0}, expectedLength={expected.Length}");
        return confirmed;
    }

    private static bool CanBackspaceByLength(string text)
    {
        // Backspace counts editing units, not UTF-16 units. Limit this fallback
        // to the plain characters used by the supported keyboard layouts.
        // Emoji, combining marks and bidi controls must not delete adjacent text.
        return text.Length > 0 && text.All(c =>
            (c >= '\u0021' && c <= '\u007e') ||
            (c >= '\u0410' && c <= '\u044f') || c == 'Ё' || c == 'ё' ||
            (c >= '\u05d0' && c <= '\u05ea') || c == '№');
    }

    private static bool DeleteLastWord(LastWordTarget target, string original, IntPtr foreground)
    {
        if (!CanBackspaceByLength(original))
        {
            Log("FAIL: last-word backspace fallback cannot safely count this text");
            return false;
        }

        if (!CorrectionWorker.CanContinue || GetForegroundWindow() != foreground ||
            !target.Element.Equals(AutomationElement.FocusedElement))
        {
            Log("FAIL: focus changed before last-word backspace fallback");
            return false;
        }

        // Collapse at the saved LOGICAL end of the word, not with Left/Right:
        // visual arrows are ambiguous for Hebrew + digits. Trailing whitespace
        // and text after the original caret remain outside the deletion.
        target.End.Select();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Thread.Sleep(10);
            if (!CorrectionWorker.CanContinue || GetForegroundWindow() != foreground ||
                !target.Element.Equals(AutomationElement.FocusedElement))
                break;

            TextPatternRange[] selections = target.Pattern.GetSelection();
            if (selections.Length != 1)
                continue;

            TextPatternRange caret = selections[0];
            if (caret.CompareEndpoints(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End) != 0 ||
                caret.CompareEndpoints(TextPatternRangeEndpoint.Start, target.End, TextPatternRangeEndpoint.Start) != 0 ||
                !TextBeforeCaretEquals(caret, original))
                continue;

            var inputs = new INPUT[checked(original.Length * 2)];
            for (int i = 0; i < original.Length; i++)
            {
                inputs[i * 2] = Key(VK_BACK, false);
                inputs[i * 2 + 1] = Key(VK_BACK, true);
            }

            Log($"Last-word replacement using backspace fallback, length={original.Length}");
            SendKeys(inputs);
            Thread.Sleep(90);
            return true;
        }

        Log("FAIL: could not confirm collapsed caret and original text before backspace fallback");
        return false;
    }

    private static string? TryGetSelectionOrSelectLastWordViaAutomation(IntPtr hwnd, out LastWordTarget? target)
    {
        target = null;
        try
        {
            AutomationElement element = AutomationElement.FocusedElement
                ?? AutomationElement.FromHandle(hwnd);

            if (element == null ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                return null;

            var textPattern = (TextPattern)patternObj;
            TextPatternRange[] selections = textPattern.GetSelection();

            if (selections != null && selections.Length > 0)
            {
                string existingSelection = selections[0].GetText(-1);
                if (!string.IsNullOrEmpty(existingSelection))
                    return existingSelection;
            }

            if (selections == null || selections.Length == 0)
                return null;

            TextPatternRange caret = selections[0];
            TextPatternRange prefix = textPattern.DocumentRange.Clone();
            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);

            string beforeCaret = prefix.GetText(-1);
            if (string.IsNullOrEmpty(beforeCaret))
                return null;

            int end = beforeCaret.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(beforeCaret[end]))
                end--;
            if (end < 0)
                return null;

            int start = end;
            while (start >= 0 && !char.IsWhiteSpace(beforeCaret[start]))
                start--;
            start++;

            int wordLength = end - start + 1;
            int trailingWhitespace = beforeCaret.Length - 1 - end;
            if (wordLength <= 0)
                return null;

            TextPatternRange wordRange = caret.Clone();
            wordRange.MoveEndpointByRange(TextPatternRangeEndpoint.End, wordRange, TextPatternRangeEndpoint.Start);

            if (trailingWhitespace > 0)
            {
                if (wordRange.Move(TextUnit.Character, -trailingWhitespace) != -trailingWhitespace)
                    return null;
            }

            if (wordRange.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -wordLength) != -wordLength)
                return null;

            string lastWordText = wordRange.GetText(-1);
            if (!string.Equals(lastWordText, beforeCaret.Substring(start, wordLength), StringComparison.Ordinal))
                return null;

            TextPatternRange wordEnd = wordRange.Clone();
            wordEnd.MoveEndpointByRange(TextPatternRangeEndpoint.Start, wordEnd, TextPatternRangeEndpoint.End);
            target = new LastWordTarget(element, textPattern, wordEnd);
            if (!element.Equals(AutomationElement.FocusedElement))
                return null;
            try
            {
                wordRange.Select();
            }
            catch (Exception ex)
            {
                // Preserve the known word and its end even when Select fails.
                Log("UIA word selection failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return lastWordText;
        }
        catch (Exception ex)
        {
            Log("UIA last-word failed: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    private static string? TryGetTextViaAutomation(IntPtr hwnd, bool selectionOnly)
    {
        try
        {
            AutomationElement element = AutomationElement.FromHandle(hwnd);
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused != null)
                element = focused;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObj))
            {
                var textPattern = (TextPattern)textPatternObj;
                TextPatternRange[] ranges = textPattern.GetSelection();

                if (ranges != null && ranges.Length > 0)
                {
                    string selected = ranges[0].GetText(-1);
                    if (!string.IsNullOrEmpty(selected))
                        return selected;
                }

                if (selectionOnly)
                    return null;
            }

            if (!selectionOnly &&
                element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObj))
            {
                string value = ((ValuePattern)valuePatternObj).Current.Value;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }
        catch (Exception ex)
        {
            Log("UIA read failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        return null;
    }

    private static void SelectAll() => SendChord(VK_CONTROL, VK_A);

    private static void SelectPreviousWordWithKeyboard()
    {
        SendKeys(new[]
        {
            Key(VK_CONTROL, false),
            Key(VK_SHIFT, false),
            Key(VK_LEFT, false),
            Key(VK_LEFT, true),
            Key(VK_SHIFT, true),
            Key(VK_CONTROL, true)
        });
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new INPUT[checked(text.Length * 2)];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = UnicodeKey(text[i], false);
            inputs[i * 2 + 1] = UnicodeKey(text[i], true);
        }
        SendKeys(inputs);
    }

    private static void Copy() => SendChord(VK_CONTROL, VK_C);
    private static bool Paste() => SendChord(VK_CONTROL, VK_V);

    private static bool SendChord(int modifier, int key)
    {
        return SendKeys(new[]
        {
            Key(modifier, false),
            Key(key, false),
            Key(key, true),
            Key(modifier, true)
        });
    }

    private static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                dwExtraInfo = GetMessageExtraInfo()
            }
        }
    };

    private static INPUT UnicodeKey(char character, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                dwExtraInfo = GetMessageExtraInfo()
            }
        }
    };

    private static bool SendKeys(INPUT[] inputs)
    {
        int inputSize = Marshal.SizeOf<INPUT>();
        uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();
}
