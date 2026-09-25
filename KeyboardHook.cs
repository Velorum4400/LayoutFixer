using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;

    private readonly LowLevelKeyboardProc _callback;
    private readonly IntPtr _hook;
    private readonly HashSet<Keys> _pressed = new();
    private readonly HashSet<Keys> _suppressedKeys = new();
    private HashSet<Keys>? _pendingKeys;
    private string _fullTextHotkey = "Ctrl+Shift";
    private HashSet<Keys> _fullTextKeys = HotkeyDefinition.Parse("Ctrl+Shift");

    public event Action? HotkeyPressed;

    public string FullTextHotkey
    {
        get => _fullTextHotkey;
        set
        {
            _fullTextHotkey = value;
            _fullTextKeys = HotkeyDefinition.Parse(value);
        }
    }

    public KeyboardHook()
    {
        _callback = HookCallback;
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback,
            GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        int message = wParam.ToInt32();
        bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
        bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
        if (!down && !up)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        Keys key = HotkeyDefinition.Normalize((Keys)data.vkCode);
        if (down)
        {
            bool firstDown = _pressed.Add(key);
            if (firstDown && _pendingKeys == null && SetEquals(_pressed, _fullTextKeys))
            {
                _pendingKeys = new HashSet<Keys>(_fullTextKeys);
                _suppressedKeys.Add(key);
                return (IntPtr)1;
            }
            if (_suppressedKeys.Contains(key))
                return (IntPtr)1;
        }

        if (up)
        {
            _pressed.Remove(key);
            bool suppress = _suppressedKeys.Remove(key);
            TryFirePending();
            if (suppress)
                return (IntPtr)1;
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void TryFirePending()
    {
        if (_pendingKeys == null)
            return;
        foreach (Keys key in _pendingKeys)
            if (_pressed.Contains(key))
                return;
        _pendingKeys = null;
        HotkeyPressed?.Invoke();
    }

    private static bool SetEquals(HashSet<Keys> current, HashSet<Keys> wanted) =>
        wanted.Count >= 1 && wanted.Count <= HotkeyDefinition.MaxKeys && current.SetEquals(wanted);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
