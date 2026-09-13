using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LayoutFixer;

internal static class ScannerTextInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;

    public static void ReplacePreviousText(string englishText, int typedLength, Keys? suffix)
    {
        if (string.IsNullOrEmpty(englishText) || typedLength <= 0)
        {
            ScannerDiagnosticLog.Write($"Injection skipped: empty text or invalid typedLength={typedLength}");
            return;
        }

        ScannerDiagnosticLog.Write(
            $"Injection begin: text='{Sample(englishText)}', typedLength={typedLength}, suffix={suffix?.ToString() ?? "None"}, method=clipboard-paste");

        IDataObject? originalClipboard = null;
        try
        {
            originalClipboard = Clipboard.GetDataObject();
        }
        catch (Exception ex)
        {
            ScannerDiagnosticLog.WriteException("Failed to capture clipboard before scanner paste", ex);
        }

        if (!TrySetClipboardText(englishText))
        {
            ScannerDiagnosticLog.Write("Injection aborted because barcode text could not be placed on clipboard.");
            return;
        }

        try
        {
            var inputs = new List<INPUT>(typedLength * 2 + 10);

            AddKeyDown(inputs, VK_SHIFT);
            for (int i = 0; i < typedLength; i++)
                AddVirtualKey(inputs, VK_LEFT);
            AddKeyUp(inputs, VK_SHIFT);

            AddKeyDown(inputs, VK_CONTROL);
            AddVirtualKey(inputs, VK_V);
            AddKeyUp(inputs, VK_CONTROL);

            Send(inputs, "select-and-paste");

            // Give the foreground application time to process Ctrl+V before
            // re-sending the scanner's original Enter/Tab suffix.
            Thread.Sleep(70);

            if (suffix is Keys.Enter or Keys.Tab)
            {
                var suffixInputs = new List<INPUT>(2);
                AddVirtualKey(suffixInputs, suffix == Keys.Enter ? VK_RETURN : VK_TAB);
                Send(suffixInputs, $"suffix-{suffix}");
            }

            Thread.Sleep(70);
        }
        finally
        {
            RestoreClipboard(originalClipboard);
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                ScannerDiagnosticLog.Write($"Scanner clipboard set successfully on attempt {attempt}.");
                return true;
            }
            catch (ExternalException ex)
            {
                if (attempt == 10)
                    ScannerDiagnosticLog.WriteException("Failed to set scanner clipboard text", ex);
                else
                    Thread.Sleep(15);
            }
        }

        return false;
    }

    private static void RestoreClipboard(IDataObject? originalClipboard)
    {
        if (originalClipboard is null)
            return;

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
                if (attempt == 10)
                    ScannerDiagnosticLog.WriteException("Failed to restore clipboard after scanner paste", ex);
                else
                    Thread.Sleep(20);
            }
        }
    }

    private static void Send(List<INPUT> inputs, string stage)
    {
        if (inputs.Count == 0)
            return;

        INPUT[] array = inputs.ToArray();
        uint sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        int error = Marshal.GetLastWin32Error();

        ScannerDiagnosticLog.Write(
            $"Injection stage '{stage}': sent={sent}, expected={array.Length}, error={error}");

        if (sent != array.Length)
            CrashLogger.Write($"Scanner SendInput incomplete at {stage}: sent={sent}, expected={array.Length}, error={error}");
    }

    private static string Sample(string value)
    {
        string text = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return text.Length <= 100 ? text : text[..100] + "...";
    }

    private static void AddVirtualKey(List<INPUT> inputs, ushort vk)
    {
        AddKeyDown(inputs, vk);
        AddKeyUp(inputs, vk);
    }

    private static void AddKeyDown(List<INPUT> inputs, ushort vk)
    {
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk }
            }
        });
    }

    private static void AddKeyUp(List<INPUT> inputs, ushort vk)
    {
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
            }
        });
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
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);
}
