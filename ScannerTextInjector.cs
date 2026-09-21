using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace LayoutFixer;

internal static class ScannerTextInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;

    public static void ReplacePreviousText(string englishText, int typedLength, Keys? suffix)
    {
        ScannerDiagnosticLog.Write("========== START scanner replacement ==========");
        try { ReplacePreviousTextCore(englishText, typedLength, suffix); }
        finally { ScannerDiagnosticLog.Write("========== END scanner replacement ==========" + Environment.NewLine); }
    }

    private static void ReplacePreviousTextCore(string englishText, int typedLength, Keys? suffix)
    {
        if (string.IsNullOrEmpty(englishText) || typedLength <= 0)
        {
            ScannerDiagnosticLog.Write($"Injection skipped: empty text or invalid typedLength={typedLength}");
            return;
        }

        ScannerDiagnosticLog.Write(
            $"Injection begin: text='{Sample(englishText)}', typedLength={typedLength}, suffix={suffix?.ToString() ?? "None"}");

        if (TryReplaceWithUiAutomation(englishText, typedLength))
        {
            ScannerDiagnosticLog.Write("Injection completed with UI Automation ValuePattern.");
            SendSuffix(suffix);
            return;
        }

        ScannerDiagnosticLog.Write("UI Automation replacement unavailable; using RemoteApp-friendly backspace-and-paste fallback.");
        ReplaceWithClipboardFallback(englishText, typedLength, suffix);
    }

    private static bool TryReplaceWithUiAutomation(string englishText, int typedLength)
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element == null)
            {
                ScannerDiagnosticLog.Write("UIA replacement unavailable: no focused AutomationElement.");
                return false;
            }

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObject))
            {
                ScannerDiagnosticLog.Write($"UIA replacement unavailable: focused control '{SafeName(element)}' has no ValuePattern.");
                return false;
            }

            var valuePattern = (ValuePattern)valuePatternObject;
            if (valuePattern.Current.IsReadOnly)
            {
                ScannerDiagnosticLog.Write($"UIA replacement unavailable: focused control '{SafeName(element)}' is read-only.");
                return false;
            }

            string currentValue = valuePattern.Current.Value ?? string.Empty;
            int caretOffset = currentValue.Length;
            bool caretFromTextPattern = false;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObject))
            {
                try
                {
                    var textPattern = (TextPattern)textPatternObject;
                    TextPatternRange[] ranges = textPattern.GetSelection();
                    if (ranges is { Length: > 0 })
                    {
                        TextPatternRange prefix = textPattern.DocumentRange.Clone();
                        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, ranges[0], TextPatternRangeEndpoint.Start);
                        string beforeCaret = prefix.GetText(-1) ?? string.Empty;
                        caretOffset = Math.Min(beforeCaret.Length, currentValue.Length);
                        caretFromTextPattern = true;
                    }
                }
                catch (Exception ex)
                {
                    ScannerDiagnosticLog.WriteException("UIA caret lookup failed; assuming caret is at end of ValuePattern text", ex);
                    caretOffset = currentValue.Length;
                }
            }

            if (caretOffset < typedLength)
            {
                ScannerDiagnosticLog.Write($"UIA replacement rejected: caretOffset={caretOffset}, typedLength={typedLength}, valueLength={currentValue.Length}.");
                return false;
            }

            int replaceStart = caretOffset - typedLength;
            string newValue = currentValue[..replaceStart] + englishText + currentValue[caretOffset..];
            ScannerDiagnosticLog.Write($"UIA replacement attempt: control='{SafeName(element)}', valueLength={currentValue.Length}, caretOffset={caretOffset}, caretFromTextPattern={caretFromTextPattern}, replaceStart={replaceStart}, typedLength={typedLength}.");
            valuePattern.SetValue(newValue);

            int desiredCaret = replaceStart + englishText.Length;
            TryRestoreUiAutomationCaret(desiredCaret);

            AutomationElement? verifyElement = AutomationElement.FocusedElement;
            if (verifyElement != null && verifyElement.TryGetCurrentPattern(ValuePattern.Pattern, out object? verifyPatternObject))
            {
                string verified = ((ValuePattern)verifyPatternObject).Current.Value ?? string.Empty;
                bool matches = string.Equals(verified, newValue, StringComparison.Ordinal);
                ScannerDiagnosticLog.Write($"UIA replacement verification: success={matches}, resultingLength={verified.Length}.");
                return matches;
            }

            ScannerDiagnosticLog.Write("UIA replacement applied; verification ValuePattern was unavailable.");
            return true;
        }
        catch (Exception ex)
        {
            ScannerDiagnosticLog.WriteException("UIA scanner replacement failed", ex);
            return false;
        }
    }

    private static void TryRestoreUiAutomationCaret(int offset)
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element == null || !element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObject)) return;
            var textPattern = (TextPattern)textPatternObject;
            TextPatternRange range = textPattern.DocumentRange.Clone();
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
            if (offset > 0) range.Move(TextUnit.Character, offset);
            range.Select();
            ScannerDiagnosticLog.Write($"UIA caret restored to offset {offset}.");
        }
        catch (Exception ex) { ScannerDiagnosticLog.WriteException("UIA caret restore failed", ex); }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            string name = element.Current.Name;
            string type = element.Current.ControlType?.ProgrammaticName ?? "unknown";
            return string.IsNullOrWhiteSpace(name) ? type : $"{name} ({type})";
        }
        catch { return "unknown"; }
    }

    private static void ReplaceWithClipboardFallback(string englishText, int typedLength, Keys? suffix)
    {
        IDataObject? originalClipboard = null;
        try { originalClipboard = Clipboard.GetDataObject(); }
        catch (Exception ex) { ScannerDiagnosticLog.WriteException("Failed to capture clipboard before scanner paste", ex); }

        if (!TrySetClipboardText(englishText))
        {
            ScannerDiagnosticLog.Write("Injection aborted because barcode text could not be placed on clipboard.");
            return;
        }

        try
        {
            // Backspace is deliberately used instead of Shift+Left selection here. RemoteApp/RDP
            // controls often expose no UIA text pattern and can ignore synthetic selection while
            // still accepting ordinary editing keys such as Backspace and Ctrl+V.
            var deleteInputs = new List<INPUT>(typedLength * 2);
            for (int i = 0; i < typedLength; i++) AddVirtualKey(deleteInputs, VK_BACK);
            Send(deleteInputs, "fallback-backspace");
            Thread.Sleep(90);

            var pasteInputs = new List<INPUT>(4);
            AddKeyDown(pasteInputs, VK_CONTROL);
            AddVirtualKey(pasteInputs, VK_V);
            AddKeyUp(pasteInputs, VK_CONTROL);
            Send(pasteInputs, "fallback-paste");
            Thread.Sleep(120);

            SendSuffix(suffix);
            Thread.Sleep(80);
        }
        finally { RestoreClipboard(originalClipboard); }
    }

    private static void SendSuffix(Keys? suffix)
    {
        if (suffix is not (Keys.Enter or Keys.Tab)) return;
        var suffixInputs = new List<INPUT>(2);
        AddVirtualKey(suffixInputs, suffix == Keys.Enter ? VK_RETURN : VK_TAB);
        Send(suffixInputs, $"suffix-{suffix}");
    }

    private static bool TrySetClipboardText(string text)
    {
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                if (!NativeClipboard.TrySetText(text)) return false;
                ScannerDiagnosticLog.Write($"Scanner clipboard set successfully on attempt {attempt}.");
                return true;
            }
            catch (ExternalException ex)
            {
                if (attempt == 10) ScannerDiagnosticLog.WriteException("Failed to set scanner clipboard text", ex);
                else Thread.Sleep(15);
            }
        }
        return false;
    }

    private static void RestoreClipboard(IDataObject? originalClipboard)
    {
        if (originalClipboard is null) return;
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(originalClipboard, true);
                ScannerDiagnosticLog.Write($"Scanner clipboard restored on attempt {attempt}.");
                return;
            }
            catch (ExternalException ex)
            {
                if (attempt == 10) ScannerDiagnosticLog.WriteException("Failed to restore clipboard after scanner paste", ex);
                else Thread.Sleep(20);
            }
        }
    }

    private static void Send(List<INPUT> inputs, string stage)
    {
        if (inputs.Count == 0) return;
        INPUT[] array = inputs.ToArray();
        uint sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        int error = Marshal.GetLastWin32Error();
        ScannerDiagnosticLog.Write($"Injection stage '{stage}': sent={sent}, expected={array.Length}, error={error}");
        if (sent != array.Length) CrashLogger.Write($"Scanner SendInput incomplete at {stage}: sent={sent}, expected={array.Length}, error={error}");
    }

    private static string Sample(string value)
    {
        string text = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return text.Length <= 100 ? text : text[..100] + "...";
    }

    private static void AddVirtualKey(List<INPUT> inputs, ushort vk) { AddKeyDown(inputs, vk); AddKeyUp(inputs, vk); }
    private static void AddKeyDown(List<INPUT> inputs, ushort vk) => inputs.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } });
    private static void AddKeyUp(List<INPUT> inputs, ushort vk) => inputs.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } });

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);
}
