using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

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

    public static bool TryFix(
        bool lastWord,
        SelectionPreserver.SelectionSnapshot? selectionSnapshot,
        out KeyboardLanguage from,
        out KeyboardLanguage to)
    {
        IntPtr targetWindow = GetForegroundWindow();
        IntPtr focusWindow = GetFocusedWindow(targetWindow);
        KeyboardLanguage? currentLayout = GetCurrentLanguage(targetWindow);

        from = currentLayout ?? KeyboardLanguage.English;
        to = from;

        string available = string.Join(",", KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.ShortName));
        Log($"START lastWord={lastWord}, target=0x{targetWindow.ToInt64():X}, focus=0x{focusWindow.ToInt64():X}, currentLayout={(currentLayout?.ToString() ?? "Unsupported")}, available=[{available}]");

        ClipboardSnapshot clipboardSnapshot = CaptureClipboardSnapshot();
        bool pasteConfirmed = false;

        try
        {
            TryClearClipboard();
            Log("Clipboard clear attempted");

            string? original;

            if (lastWord)
            {
                original = TryGetSelectionOrSelectLastWordViaAutomation(
                    focusWindow != IntPtr.Zero ? focusWindow : targetWindow);
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

            if (string.IsNullOrEmpty(original))
            {
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

            if (!SetClipboardTextWithRetry(converted))
            {
                Log("FAIL: could not write converted text to clipboard");
                return false;
            }

            Log("Converted text placed into clipboard");

            if (!Paste())
            {
                Log("FAIL: SendInput paste returned false");
                return false;
            }

            Log("Ctrl+V sent");

            pasteConfirmed = WaitForPasteCompletion(
                focusWindow != IntPtr.Zero ? focusWindow : targetWindow,
                original,
                converted,
                selectionSnapshot);

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
            RestoreClipboardSnapshot(clipboardSnapshot, pasteConfirmed);
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

    private static void RestoreClipboardSnapshot(ClipboardSnapshot snapshot, bool pasteConfirmed)
    {
        if (!snapshot.Captured)
            return;

        if (pasteConfirmed)
        {
            RestoreClipboardSnapshotNow(snapshot);
            return;
        }

        RestoreClipboardSnapshotDelayed(snapshot);
    }

    private static async void RestoreClipboardSnapshotDelayed(ClipboardSnapshot snapshot)
    {
        try
        {
            // Keep the converted text in the clipboard long enough for slower
            // applications to consume Ctrl+V before the original clipboard is restored.
            await Task.Delay(180);
            RestoreClipboardSnapshotNow(snapshot);
        }
        catch (Exception ex)
        {
            Log($"Delayed clipboard restore failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RestoreClipboardSnapshotNow(ClipboardSnapshot snapshot)
    {
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

    private static bool WaitForPasteCompletion(
        IntPtr hwnd,
        string original,
        string converted,
        SelectionPreserver.SelectionSnapshot? selectionSnapshot)
    {
        bool sawTextPattern = false;

        for (int attempt = 0; attempt < 18; attempt++)
        {
            try
            {
                AutomationElement? element = AutomationElement.FocusedElement;
                if (element == null && hwnd != IntPtr.Zero)
                    element = AutomationElement.FromHandle(hwnd);

                if (element != null &&
                    element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                {
                    sawTextPattern = true;
                    var textPattern = (TextPattern)patternObj;

                    if (selectionSnapshot?.HasSelection == true)
                    {
                        if (RangeTextEquals(textPattern, selectionSnapshot.StartOffset, converted))
                        {
                            Log($"Paste completion confirmed at selection on attempt {attempt + 1}");
                            return true;
                        }
                    }
                    else
                    {
                        string documentText = textPattern.DocumentRange.GetText(-1);
                        string normalizedDoc = documentText.TrimEnd('\r', '\n');
                        string normalizedConverted = converted.TrimEnd('\r', '\n');

                        if (string.Equals(normalizedDoc, normalizedConverted, StringComparison.Ordinal))
                        {
                            Log($"Paste completion confirmed for full text on attempt {attempt + 1}");
                            return true;
                        }

                        if (documentText.Contains(converted, StringComparison.Ordinal) &&
                            !documentText.Contains(original, StringComparison.Ordinal))
                        {
                            Log($"Paste completion confirmed in document on attempt {attempt + 1}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Paste completion UIA check failed: {ex.GetType().Name}");
                break;
            }

            Thread.Sleep(10);
        }

        Log($"Paste completion not confirmed; textPattern={sawTextPattern}. Clipboard restore will be delayed.");
        return false;
    }

    private static bool RangeTextEquals(TextPattern textPattern, int startOffset, string expected)
    {
        TextPatternRange range = textPattern.DocumentRange.Clone();
        range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);

        if (startOffset > 0 && range.Move(TextUnit.Character, startOffset) != startOffset)
            return false;

        int extended = range.MoveEndpointByUnit(
            TextPatternRangeEndpoint.End,
            TextUnit.Character,
            expected.Length);

        if (extended != expected.Length)
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
                        return value;
                }
            }
            catch (ExternalException) { }

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
                wordRange.Move(TextUnit.Character, -trailingWhitespace);

            if (wordRange.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -wordLength) == 0)
                return null;

            string lastWordText = wordRange.GetText(-1);
            if (string.IsNullOrEmpty(lastWordText))
                return null;

            wordRange.Select();
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
