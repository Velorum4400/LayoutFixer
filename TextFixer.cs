using System;
using System.Diagnostics;
using System.IO;
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
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint INPUT_KEYBOARD = 1;

    public static bool TryFixAllText(
        out KeyboardLanguage from,
        out KeyboardLanguage to,
        IntPtr expectedWindow = default)
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
        Log($"========== START mode=fullText, target=0x{targetWindow.ToInt64():X}, focus=0x{focusWindow.ToInt64():X}, currentLayout={(currentLayout?.ToString() ?? "Unsupported")}, available=[{available}]");
        ClipboardSnapshot? clipboardSnapshot = null;
        bool clipboardChanged = false;

        void EnsureClipboardSnapshot()
        {
            if (clipboardSnapshot != null)
                return;
            clipboardSnapshot = CaptureClipboardSnapshot();
            Log($"TIMING clipboard snapshot: {elapsed.ElapsedMilliseconds} ms");
        }

        try
        {
            if (!HasExpectedFocus(targetWindow, focusWindow))
            {
                Log("FAIL: focus changed while preparing correction");
                return false;
            }

            SelectAll();
            Log("Ctrl+A sent");
            Thread.Sleep(180);
            string? original = TryGetTextViaAutomation(focusWindow != IntPtr.Zero ? focusWindow : targetWindow);

            if (!HasExpectedFocus(targetWindow, focusWindow))
            {
                Log("FAIL: focus changed while reading text");
                return false;
            }

            if (string.IsNullOrEmpty(original))
            {
                EnsureClipboardSnapshot();
                uint sequence = GetClipboardSequenceNumber();
                Copy();
                original = WaitForClipboardText(sequence, 12);
                clipboardChanged = !string.IsNullOrEmpty(original);
            }

            if (string.IsNullOrEmpty(original))
            {
                Log("FAIL: no text obtained");
                return false;
            }

            Log($"Text obtained, length={original.Length}, sample={Sample(original)}");
            Log($"TIMING text acquired: {elapsed.ElapsedMilliseconds} ms");
            KeyboardLanguage fallback = currentLayout ?? KeyboardLanguage.English;
            from = LayoutConverter.DetectLanguage(original, fallback);
            if (!KeyboardLayout.TryGetCorrectionTarget(from, currentLayout, out to))
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

            if (!HasExpectedFocus(targetWindow, focusWindow))
            {
                Log("FAIL: focus changed before replacement");
                return false;
            }

            Log($"TIMING clipboard publish start: {elapsed.ElapsedMilliseconds} ms");
            EnsureClipboardSnapshot();
            if (!NativeClipboard.TrySetText(converted))
            {
                Log("FAIL: could not write converted text to clipboard");
                return false;
            }
            clipboardChanged = true;
            Log("Converted text placed into clipboard");
            Log($"TIMING clipboard publish end: {elapsed.ElapsedMilliseconds} ms");

            if (!HasExpectedFocus(targetWindow, focusWindow))
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

            if (!WaitForPasteCompletion(focusWindow != IntPtr.Zero ? focusWindow : targetWindow, converted))
            {
                Log("Paste not confirmed through UI Automation; waiting before clipboard restore");
                Thread.Sleep(180);
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
            if (clipboardChanged && clipboardSnapshot != null)
                RestoreClipboardSnapshot(clipboardSnapshot);
            Log($"TIMING total: {elapsed.ElapsedMilliseconds} ms");
            Log("========== END ==========" + Environment.NewLine);
        }
    }

    private static bool HasExpectedFocus(IntPtr targetWindow, IntPtr focusWindow) =>
        CorrectionWorker.CanContinue && GetForegroundWindow() == targetWindow &&
        GetFocusedWindow(targetWindow) == focusWindow;

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
                            long oldPosition = stream.CanSeek ? stream.Position : 0;
                            if (stream.CanSeek) stream.Position = 0;
                            var clone = new MemoryStream();
                            stream.CopyTo(clone);
                            clone.Position = 0;
                            if (stream.CanSeek) stream.Position = oldPosition;
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
            catch (ExternalException) { Thread.Sleep(50); }
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
            catch (ExternalException) { Thread.Sleep(50); }
            catch (Exception ex)
            {
                Log($"Clipboard restore failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }
    }

    private static string? WaitForClipboardText(uint initialSequence, int attempts)
    {
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < attempts * 75)
        {
            try
            {
                if (GetClipboardSequenceNumber() != initialSequence &&
                    Clipboard.ContainsText(TextDataFormat.UnicodeText))
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

    private static bool WaitForPasteCompletion(IntPtr hwnd, string converted)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            try
            {
                AutomationElement? element = AutomationElement.FocusedElement;
                if (element == null && hwnd != IntPtr.Zero)
                    element = AutomationElement.FromHandle(hwnd);
                if (element != null && element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj))
                {
                    string documentText = ((TextPattern)patternObj).DocumentRange.GetText(-1);
                    if (string.Equals(documentText.TrimEnd('\r', '\n'),
                        converted.TrimEnd('\r', '\n'), StringComparison.Ordinal))
                    {
                        Log($"Paste completion confirmed for full text on attempt {attempt + 1}");
                        return true;
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

    private static string? TryGetTextViaAutomation(IntPtr hwnd)
    {
        try
        {
            AutomationElement element = AutomationElement.FromHandle(hwnd);
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused != null)
                element = focused;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObj))
            {
                TextPatternRange[] ranges = ((TextPattern)textPatternObj).GetSelection();
                if (ranges != null && ranges.Length > 0)
                {
                    string selected = ranges[0].GetText(-1);
                    if (!string.IsNullOrEmpty(selected))
                        return selected;
                }
            }
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObj))
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

    private static KeyboardLanguage? GetCurrentLanguage(IntPtr hwnd)
    {
        uint threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        IntPtr hkl = GetKeyboardLayout(threadId);
        return KeyboardLayout.TryGetLanguage(hkl, out KeyboardLanguage language) ? language : null;
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
        string sample = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return sample.Length <= 80 ? sample : sample[..80] + "...";
    }

    private static void Log(string message) => DiagnosticLogStore.Write(message);
    private static void SelectAll() => SendChord(VK_CONTROL, VK_A);
    private static void Copy() => SendChord(VK_CONTROL, VK_C);
    private static bool Paste() => SendChord(VK_CONTROL, VK_V);

    private static bool SendChord(int modifier, int key) => SendKeys(new[]
    {
        Key(modifier, false), Key(key, false), Key(key, true), Key(modifier, true)
    });

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
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
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
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT
    { public uint uMsg; public ushort wParamL, wParamH; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
}
