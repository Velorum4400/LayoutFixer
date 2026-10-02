using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class HotkeyService : IDisposable
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
    private readonly HashSet<Keys> _suppressed = new();
    private HashSet<Keys>? _pending;
    private string _hotkey = "Ctrl+Shift";
    private HashSet<Keys> _keys = HotkeyDefinition.Parse("Ctrl+Shift");
    private bool _modifierOnly = true;
    private long _callbackCount;
    private long _lastCallbackUtcTicks;
    private int _lastVirtualKey = -1;
    private int _lastMessage = -1;
    private int _lastFlags;
    private int _lastInjected;
    private readonly uint _installThreadId;
    private int _disposed;

    public event Action? Pressed;
    public IntPtr HookHandle => _hook;
    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && _hook != IntPtr.Zero;
    public long CallbackCount => Interlocked.Read(ref _callbackCount);
    public DateTime LastCallbackUtc => new(Interlocked.Read(ref _lastCallbackUtcTicks), DateTimeKind.Utc);
    public int LastVirtualKey => Volatile.Read(ref _lastVirtualKey);
    public int LastMessage => Volatile.Read(ref _lastMessage);
    public uint LastFlags => unchecked((uint)Volatile.Read(ref _lastFlags));
    public bool LastInjected => Volatile.Read(ref _lastInjected) != 0;
    public uint InstallThreadId => _installThreadId;
    public string Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            _keys = HotkeyDefinition.Parse(value);
            _modifierOnly = HotkeyDefinition.IsModifierOnly(_keys);
            _pressed.Clear();
            _suppressed.Clear();
            _pending = null;
        }
    }

    public HotkeyService()
    {
        _callback = HookCallback;
        _installThreadId = GetCurrentThreadId();
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        DiagnosticLogStore.Write($"HotkeyService Register: hotkey={_hotkey}, hook=0x{_hook.ToInt64():X}, installThreadId={_installThreadId}, result=True, error=0");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        Interlocked.Increment(ref _callbackCount);
        Interlocked.Exchange(ref _lastCallbackUtcTicks, DateTime.UtcNow.Ticks);
        try
        {
            if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            Volatile.Write(ref _lastVirtualKey, unchecked((int)data.vkCode));
            Volatile.Write(ref _lastMessage, wParam.ToInt32());
            Volatile.Write(ref _lastFlags, unchecked((int)data.flags));
            Volatile.Write(ref _lastInjected,
                (data.flags & LLKHF_INJECTED) != 0 ? 1 : 0);
            if ((data.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
            int message = wParam.ToInt32(); bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN; bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
            if (!down && !up) return CallNextHookEx(_hook, nCode, wParam, lParam);
            Keys key = HotkeyDefinition.Normalize((Keys)data.vkCode);
            if (down)
            {
                bool first = _pressed.Add(key);
                if (first && _pending != null && !_pending.Contains(key)) _pending = null;
                if (first && _pending == null && _pressed.SetEquals(_keys))
                {
                    _pending = new HashSet<Keys>(_keys);
                    if (!_modifierOnly) { _suppressed.Add(key); return (IntPtr)1; }
                }
                if (_suppressed.Contains(key)) return (IntPtr)1;
            }
            if (up)
            {
                _pressed.Remove(key); bool suppress = _suppressed.Remove(key);
                if (_pending != null && !_pending.Overlaps(_pressed)) { _pending = null; DiagnosticLogStore.Write($"Hotkey pressed: hotkey={_hotkey}, hook=0x{_hook.ToInt64():X}"); Pressed?.Invoke(); }
                if (suppress) return (IntPtr)1;
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        catch (Exception ex) { DiagnosticLogStore.Write($"Hotkey hook callback exception: hotkey={_hotkey}, type={ex.GetType().Name}, message={ex.Message}"); return CallNextHookEx(_hook, nCode, wParam, lParam); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || _hook == IntPtr.Zero) return;
        bool result = UnhookWindowsHookEx(_hook);
        DiagnosticLogStore.Write($"HotkeyService Unregister: hotkey={_hotkey}, hook=0x{_hook.ToInt64():X}, result={result}, error={(result ? 0 : Marshal.GetLastWin32Error())}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}

