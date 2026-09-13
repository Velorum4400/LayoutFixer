using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutFixer;

public static class TextFixer
{
    private const int VK_CONTROL = 0x11;
    private const int VK_A = 0x41;
    private const int VK_C = 0x43;
    private const int VK_V = 0x56;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint WM_COPY = 0x0301;
    private const uint INPUT_KEYBOARD = 1;

    public static bool TryFix(bool lastWord, out KeyboardLanguage from, out KeyboardLanguage to)
    {
        IntPtr targetWindow = GetForegroundWindow();
        IntPtr focusWindow = GetFocusedWindow(targetWindow);

        KeyboardLanguage? currentLayout = GetCurrentLanguage(targetWindow);

        // Provisional values required by the out parameters.
        from = currentLayout ?? KeyboardLanguage.English;
        to = from;

        string available = string.Join(
            ",",
            KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.ShortName));

        Log($"START lastWord={lastWord}, target=0x{targetWindow.ToInt64():X}, focus=0x{focusWindow.ToInt64():X}, currentLayout={(currentLayout?.ToString() ?? "Unsupported")}, available=[{available}]");

        ClipboardSnapshot clipboardSnapshot = CaptureClipboardSnapshot();
        Log($"Clipboard snapshot captured={clipboardSnapshot.Captured}, hasData={clipboardSnapshot.HasData}, formats={clipboardSnapshot.FormatCount}");

        try
        {
            uint seqBefore = GetClipboardSequenceNumber();
            Log($"Clipboard sequence before={seqBefore}");

            TryClearClipboard();
            Log("Clipboard clear attempted");

            string? original = null;

            if (lastWord)
            {
                original = TryGetSelectionOrSelectLastWordViaAutomation(
                    focusWindow != IntPtr.Zero ? focusWindow : targetWindow);

                if (!string.IsNullOrEmpty(original))
                    Log($"Selected text / last word obtained through UI Automation, length={original.Length}");
                else
                    Log("UI Automation could not obtain selected text or select the last word");
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

            if (!string.IsNullOrEmpty(original))
            {
                Log($"UI Automation text obtained, length={original.Length}, selectionOnly={lastWord}");
            }
            else
            {
                uint seqCopyStart = GetClipboardSequenceNumber();
                Copy();
                Log($"Ctrl+C sent; sequence immediately={GetClipboardSequenceNumber()}");

                original = WaitForClipboardText(seqCopyStart, 12);

                if (string.IsNullOrEmpty(original) && focusWindow != IntPtr.Zero)
                {
                    Log("Ctrl+C produced no clipboard text; trying WM_COPY on focused control");
                    SendMessage(focusWindow, WM_COPY, IntPtr.Zero, IntPtr.Zero);
                    original = WaitForClipboardText(GetClipboardSequenceNumber(), 20, allowSameSequence: true);
                }
            }

            if (string.IsNullOrEmpty(original))
            {
                Log("FAIL: no text obtained via UI Automation, Ctrl+C, or WM_COPY");
                return false;
            }

            Log($"Clipboard text obtained, length={original.Length}, sample={Sample(original)}");

            // Ctrl+Shift can already have changed the Windows layout before this code runs.
            // Therefore the source language must be inferred from the characters that were
            // actually typed, not from the active Windows layout.
            KeyboardLanguage fallback =
                currentLayout ?? KeyboardLanguage.English;

            from = LayoutConverter.DetectLanguage(original, fallback);

            // Never convert toward a language that is not installed.
            // If Windows already switched to another supported/installed layout,
            // use that as the target. Otherwise use the next language from the
            // startup snapshot of installed EN/RU/HE layouts.
            if (currentLayout.HasValue &&
                currentLayout.Value != from &&
                KeyboardLayout.IsAvailable(currentLayout.Value))
            {
                to = currentLayout.Value;
            }
            else
            {
                if (!KeyboardLayout.TryGetNext(from, out to))
                {
                    Log($"FAIL: fewer than two supported installed layouts; source={from}");
                    return false;
                }
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

            if (!SetClipboardTextWithRetry(converted))
            {
                Log("FAIL: could not write converted text to clipboard");
                return false;
            }

            // Clipboard.SetText is synchronous. The old fixed 120 ms delay here
            // only added latency before Ctrl+V and is not needed once SetText succeeds.
            Log("Converted text placed into clipboard");

            if (!Paste())
            {
                Log("FAIL: SendInput paste returned false");
                return false;
            }

            Log("Ctrl+V sent");

            // Do not always wait a fixed 180 ms after paste. Observe the UIA
            // selection/caret and continue as soon as the foreground app has
            // applied the replacement. Controls without TextPattern keep a
            // short conservative fallback wait so clipboard restoration remains safe.
            WaitForPasteCompletion(
                focusWindow != IntPtr.Zero ? focusWindow : targetWindow,
                original,
                converted);

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
            RestoreClipboardSnapshot(clipboardSnapshot);
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
                {
                    return new ClipboardSnapshot
                    {
                        Captured = true,
                        HasData = false,
                        FormatCount = 0
                    };
                }

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

                        // Streams may point to clipboard-owned memory. Clone them
                        // so the snapshot remains valid after the clipboard changes.
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
            catch (ExternalException ex)
            {
                Log($"Clipboard snapshot locked on attempt {attempt + 1}: 0x{ex.ErrorCode:X8}");
                Thread.Sleep(50);
            }
        }

        Log("Clipboard snapshot could not be captured; preserving clipboard is not guaranteed for this operation");
        return new ClipboardSnapshot
        {
            Captured = false,
            HasData = false,
            FormatCount = 0
        };
    }

    private static void RestoreClipboardSnapshot(ClipboardSnapshot snapshot)
    {
        if (!snapshot.Captured)
        {
            Log("Clipboard restore skipped because the original clipboard could not be captured safely");
            return;
        }

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
            catch (ExternalException ex)
            {
                Log($"Clipboard restore locked on attempt {attempt + 1}: 0x{ex.ErrorCode:X8}");
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Log($"Clipboard restore failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }

        Log("Clipboard restore failed after retries");
    }

    private static KeyboardLanguage? GetCurrentLanguage(IntPtr hwnd)
    {
        uint threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        IntPtr hkl = GetKeyboardLayout(threadId);

        return KeyboardLayout.TryGetLanguage(
            hkl,
            out KeyboardLanguage language)
            ? language
            : null;
    }

    private static void SwitchForegroundLayout(
        IntPtr hwnd,
        IntPtr focusHwnd,
        KeyboardLanguage language)
    {
        if (!KeyboardLayout.TryGetInstalledHkl(language, out IntPtr hkl))
        {
            Log($"Switch skipped: {language} is not installed");
            return;
        }

        if (focusHwnd != IntPtr.Zero)
            PostMessage(
                focusHwnd,
                WM_INPUTLANGCHANGEREQUEST,
                IntPtr.Zero,
                hkl);

        if (hwnd != IntPtr.Zero && hwnd != focusHwnd)
            PostMessage(
                hwnd,
                WM_INPUTLANGCHANGEREQUEST,
                IntPtr.Zero,
                hkl);
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

    private static void WaitForPasteCompletion(
        IntPtr hwnd,
        string original,
        string converted)
    {
        bool observedTextPattern = false;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                AutomationElement? element = AutomationElement.FocusedElement;
                if (element == null && hwnd != IntPtr.Zero)
                    element = AutomationElement.FromHandle(hwnd);

                if (element != null &&
                    element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                {
                    observedTextPattern = true;
                    var textPattern = (TextPattern)patternObj;
                    TextPatternRange[] ranges = textPattern.GetSelection();

                    if (ranges != null && ranges.Length > 0)
                    {
                        string selected = ranges[0].GetText(-1);

                        // Before the paste is processed the old selected text is
                        // normally still exposed. A collapsed caret, the converted
                        // text, or any changed selection means the edit was applied.
                        if (string.IsNullOrEmpty(selected) ||
                            string.Equals(selected, converted, StringComparison.Ordinal) ||
                            !string.Equals(selected, original, StringComparison.Ordinal))
                        {
                            Log($"Paste completion observed through UI Automation on attempt {attempt + 1}");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Paste completion UIA check failed: {ex.GetType().Name}");
                break;
            }

            Thread.Sleep(12);
        }

        // If UI Automation cannot tell us when the target consumed Ctrl+V,
        // retain a short fallback delay before restoring the user's clipboard.
        Thread.Sleep(observedTextPattern ? 20 : 80);
    }

    private static string? WaitForClipboardText(uint initialSequence, int attempts, bool allowSameSequence = false)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                uint seq = GetClipboardSequenceNumber();
                bool changed = seq != initialSequence;

                if ((changed || allowSameSequence) && Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    string value = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (!string.IsNullOrEmpty(value))
                    {
                        Log($"Clipboard read succeeded on attempt {i + 1}, sequence={seq}, changed={changed}");
                        return value;
                    }
                }
            }
            catch (ExternalException ex)
            {
                Log($"Clipboard read locked on attempt {i + 1}: 0x{ex.ErrorCode:X8}");
            }

            Thread.Sleep(75);
        }

        return null;
    }

    private static bool SetClipboardTextWithRetry(string text)
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    private static IntPtr GetFocusedWindow(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
            return IntPtr.Zero;

        uint targetThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var info = new GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>()
        };

        if (GetGUIThreadInfo(targetThread, ref info))
            return info.hwndFocus != IntPtr.Zero ? info.hwndFocus : foreground;

        return foreground;
    }

    private static string Sample(string value)
    {
        string s = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length <= 80 ? s : s[..80] + "...";
    }

    private static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LayoutFixer");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "diagnostic.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }

    private static string? TryGetSelectionOrSelectLastWordViaAutomation(IntPtr hwnd)
    {
        try
        {
            AutomationElement element = AutomationElement.FocusedElement
                ?? AutomationElement.FromHandle(hwnd);

            if (element == null)
                return null;

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
            {
                Log("UIA last-word: focused element has no TextPattern");
                return null;
            }

            var textPattern = (TextPattern)patternObj;
            TextPatternRange[] selections = textPattern.GetSelection();

            // If the user already selected text, preserve that exact selection
            // and correct only it. Only fall back to the last word when the
            // selection is empty (caret only).
            if (selections != null && selections.Length > 0)
            {
                string existingSelection = selections[0].GetText(-1);
                if (!string.IsNullOrEmpty(existingSelection))
                {
                    Log($"UIA existing selection obtained, length={existingSelection.Length}, sample={Sample(existingSelection)}");
                    return existingSelection;
                }
            }

            if (selections == null || selections.Length == 0)
            {
                Log("UIA last-word: no caret/selection range");
                return null;
            }

            // Even with no visible selection, TextPattern normally returns a
            // degenerate range at the caret. Build a prefix range from the
            // document start to that caret so we can locate the previous token.
            TextPatternRange caret = selections[0];
            TextPatternRange prefix = textPattern.DocumentRange.Clone();
            prefix.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                caret,
                TextPatternRangeEndpoint.Start);

            string beforeCaret = prefix.GetText(-1);
            if (string.IsNullOrEmpty(beforeCaret))
            {
                Log("UIA last-word: no text before caret");
                return null;
            }

            int end = beforeCaret.Length - 1;

            // If the caret is after spaces/newlines, ignore those and use the
            // nearest token before them.
            while (end >= 0 && char.IsWhiteSpace(beforeCaret[end]))
                end--;

            if (end < 0)
            {
                Log("UIA last-word: only whitespace before caret");
                return null;
            }

            int start = end;
            while (start >= 0 && !char.IsWhiteSpace(beforeCaret[start]))
                start--;

            start++;

            int wordLength = end - start + 1;
            int trailingWhitespace = beforeCaret.Length - 1 - end;

            if (wordLength <= 0)
                return null;

            // Start from the caret, move the whole range left over trailing
            // whitespace (if any), then extend its start over exactly one token.
            TextPatternRange wordRange = caret.Clone();

            // Ensure the range is collapsed at the caret start.
            wordRange.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                wordRange,
                TextPatternRangeEndpoint.Start);

            if (trailingWhitespace > 0)
            {
                int moved = wordRange.Move(TextUnit.Character, -trailingWhitespace);
                Log($"UIA last-word: moved over trailing whitespace={moved}");
            }

            int extended = wordRange.MoveEndpointByUnit(
                TextPatternRangeEndpoint.Start,
                TextUnit.Character,
                -wordLength);

            if (extended == 0)
            {
                Log("UIA last-word: failed to extend range backward");
                return null;
            }

            string lastWordText = wordRange.GetText(-1);
            if (string.IsNullOrEmpty(lastWordText))
            {
                Log("UIA last-word: computed range is empty");
                return null;
            }

            wordRange.Select();
            Log($"UIA last-word selected: length={lastWordText.Length}, sample={Sample(lastWordText)}");
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
            if (element == null)
                return null;

            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused != null)
                element = focused;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObj))
            {
                var textPattern = (TextPattern)textPatternObj;
                var ranges = textPattern.GetSelection();

                if (ranges != null && ranges.Length > 0)
                {
                    string selected = ranges[0].GetText(-1);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        Log($"UIA selection obtained, length={selected.Length}");
                        return selected;
                    }
                }

                if (selectionOnly)
                {
                    Log("UIA did not expose a non-empty selection");
                    return null;
                }
            }

            // ValuePattern returns the full contents of the control.
            // Never use it for "last word", because that would replace the whole field.
            if (!selectionOnly &&
                element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObj))
            {
                var valuePattern = (ValuePattern)valuePatternObj;
                string value = valuePattern.Current.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    Log($"UIA full value obtained, length={value.Length}");
                    return value;
                }
            }
        }
        catch (Exception ex)
        {
            Log("UIA read failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        return null;
    }

    private static void SelectAll()
    {
        SendChord(VK_CONTROL, VK_A);
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
                wScan = 0,
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                time = 0,
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

    // INPUT contains a native union. On 64-bit Windows the union must be
    // large enough for MOUSEINPUT (32 bytes), otherwise sizeof(INPUT) becomes
    // 32 instead of the required 40 and SendInput fails with ERROR_INVALID_PARAMETER.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
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
