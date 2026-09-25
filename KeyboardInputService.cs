using System;
using System.Runtime.InteropServices;

namespace LayoutFixer;

internal static class KeyboardInputService
{
    private const int VK_CONTROL = 0x11;
    private const int VK_A = 0x41;
    private const int VK_C = 0x43;
    private const int VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static bool SelectAll() => SendChord(VK_CONTROL, VK_A);
    public static bool Copy() => SendChord(VK_CONTROL, VK_C);
    public static bool Paste() => SendChord(VK_CONTROL, VK_V);

    private static bool SendChord(int modifier, int key)
    {
        INPUT[] inputs =
        {
            CreateKey(modifier, false), CreateKey(key, false),
            CreateKey(key, true), CreateKey(modifier, true)
        };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT CreateKey(int virtualKey, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        union = new InputUnion
        {
            keyboard = new KEYBDINPUT
            {
                virtualKey = (ushort)virtualKey,
                flags = keyUp ? KEYEVENTF_KEYUP : 0,
                extraInfo = GetMessageExtraInfo()
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public HARDWAREINPUT hardware;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    { public int dx, dy; public uint mouseData, flags, time; public IntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    { public ushort virtualKey, scanCode; public uint flags, time; public IntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT
    { public uint message; public ushort parameterLow, parameterHigh; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
}
