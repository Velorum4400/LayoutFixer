using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutFixer;

internal static class ScannerTextInjector
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;

    public static void ReplacePreviousText(string englishText, int typedLength, Keys? suffix)
    {
        if (string.IsNullOrEmpty(englishText) || typedLength <= 0)
            return;

        var inputs = new List<INPUT>(typedLength * 2 + englishText.Length * 2 + 4);

        for (int i = 0; i < typedLength; i++)
            AddVirtualKey(inputs, VK_BACK);

        foreach (char c in englishText)
            AddUnicode(inputs, c);

        if (suffix == Keys.Enter)
            AddVirtualKey(inputs, VK_RETURN);
        else if (suffix == Keys.Tab)
            AddVirtualKey(inputs, VK_TAB);

        if (inputs.Count == 0)
            return;

        INPUT[] array = inputs.ToArray();
        uint sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        if (sent != array.Length)
            CrashLogger.Write($"Scanner SendInput incomplete: sent={sent}, expected={array.Length}, error={Marshal.GetLastWin32Error()}");
    }

    private static void AddVirtualKey(List<INPUT> inputs, ushort vk)
    {
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk }
            }
        });

        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
            }
        });
    }

    private static void AddUnicode(List<INPUT> inputs, char c)
    {
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE }
            }
        });

        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
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
