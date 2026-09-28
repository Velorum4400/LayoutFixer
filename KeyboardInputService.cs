using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LayoutFixer;

internal static class KeyboardInputService
{
    private const int VK_CONTROL = 0x11;
    private const int VK_A = 0x41;
    private const int VK_C = 0x43;
    private const int VK_V = 0x56;
    private const int VK_SHIFT = 0x10;
    private const int VK_LEFT = 0x25;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly HashSet<int> ExtendedVirtualKeys = new()
    {
        // Navigation/editing cluster, right-side modifiers, Windows/App keys,
        // and keypad divide are emitted with the E0 extended-key prefix.
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
        0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0xA3, 0xA5
    };

    public static bool SelectAll() => SendChord(VK_CONTROL, VK_A);
    public static bool Copy() => SendChord(VK_CONTROL, VK_C);
    public static bool Paste() => SendChord(VK_CONTROL, VK_V);
    public static bool SelectPreviousWord() => SendChord(VK_CONTROL, VK_SHIFT, VK_LEFT);

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

    private static bool SendChord(int modifier1, int modifier2, int key)
    {
        INPUT[] inputs =
        {
            CreateKey(modifier1, false), CreateKey(modifier2, false), CreateKey(key, false),
            CreateKey(key, true), CreateKey(modifier2, true), CreateKey(modifier1, true)
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
                flags = GetKeyboardEventFlags(virtualKey, keyUp),
                extraInfo = GetMessageExtraInfo()
            }
        }
    };

    internal static uint GetKeyboardEventFlags(int virtualKey, bool keyUp)
    {
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        return ExtendedVirtualKeys.Contains(virtualKey) ? flags | KEYEVENTF_EXTENDEDKEY : flags;
    }

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

