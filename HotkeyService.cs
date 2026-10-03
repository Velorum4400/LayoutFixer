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
    private readonly string _diagnosticName;
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
    private string? _lastRejectedSignature;
    private long _lastRejectedUtcTicks;

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
    public string DiagnosticName => _diagnosticName;
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

    public HotkeyService(string diagnosticName = "Unknown")
    {
        _diagnosticName = diagnosticName;
        _callback = HookCallback;
        _installThreadId = GetCurrentThreadId();
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        DiagnosticLogStore.Write($"HotkeyService Register: hotkey={_hotkey}, hook=0x{_hook.ToInt64():X}, installThreadId={_installThreadId}, result=True, error=0");
        HotkeyWatchdogLog.WriteHealthy($"HOTKEY HOOK INSTALLED: hook={_diagnosticName}, handle=0x{_hook.ToInt64():X}, managedThreadId={Environment.CurrentManagedThreadId}, osThreadId={_installThreadId}");
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
            int message = wParam.ToInt32();
            bool injected = (data.flags & LLKHF_INJECTED) != 0;
            Keys key = HotkeyDefinition.Normalize((Keys)data.vkCode);
            if (injected)
            {
                RecordDecision(FormatMessage(message), key, true, null, Snapshot(_pressed), Snapshot(_pressed),
                    Snapshot(_pending), Snapshot(_pending), Snapshot(_suppressed), Snapshot(_suppressed), null, null,
                    "injected-ignored");
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }
            bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
            if (!down && !up)
            {
                RecordDecision(FormatMessage(message), key, false, null, Snapshot(_pressed), Snapshot(_pressed),
                    Snapshot(_pending), Snapshot(_pending), Snapshot(_suppressed), Snapshot(_suppressed), null, null,
                    "message-ignored");
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }
            string pressedBefore = Snapshot(_pressed);
            string pendingBefore = Snapshot(_pending);
            string suppressedBefore = Snapshot(_suppressed);
            if (down)
            {
                bool first = _pressed.Add(key);
                bool pendingCancelled = first && _pending != null && !_pending.Contains(key);
                if (pendingCancelled) _pending = null;
                bool setEquals = _pressed.SetEquals(_keys);
                string decision;
                if (!first)
                    decision = "already-pressed";
                else if (pendingCancelled)
                    decision = "pending-cancelled-extra-key";
                else if (!setEquals)
                    decision = "extra-key";
                else
                    decision = "waiting-keyup";
                if (first && _pending == null && setEquals)
                {
                    _pending = new HashSet<Keys>(_keys);
                    decision = "pending-created";
                    if (!_modifierOnly)
                    {
                        _suppressed.Add(key);
                        RecordDecision(FormatMessage(message), key, false, first, pressedBefore, Snapshot(_pressed),
                            pendingBefore, Snapshot(_pending), suppressedBefore, Snapshot(_suppressed), setEquals, null,
                            decision);
                        return (IntPtr)1;
                    }
                }
                if (_suppressed.Contains(key))
                {
                    RecordDecision(FormatMessage(message), key, false, first, pressedBefore, Snapshot(_pressed),
                        pendingBefore, Snapshot(_pending), suppressedBefore, Snapshot(_suppressed), setEquals, null,
                        "suppressed");
                    return (IntPtr)1;
                }
                RecordDecision(FormatMessage(message), key, false, first, pressedBefore, Snapshot(_pressed),
                    pendingBefore, Snapshot(_pending), suppressedBefore, Snapshot(_suppressed), setEquals, null, decision);
                if (IsTargetHotkeyRejection(_pressed, _keys, key, decision))
                    ReportRejected(key, pressedBefore, Snapshot(_pressed), pendingBefore, "extra-key");
            }
            if (up)
            {
                _pressed.Remove(key); bool suppress = _suppressed.Remove(key);
                bool pendingOverlaps = _pending != null && _pending.Overlaps(_pressed);
                string decision = "key-released";
                if (_pending != null && !pendingOverlaps)
                {
                    _pending = null;
                    decision = "fired";
                    RecordDecision(FormatMessage(message), key, false, null, pressedBefore, Snapshot(_pressed),
                        pendingBefore, Snapshot(_pending), suppressedBefore, Snapshot(_suppressed), null, false, decision);
                    DiagnosticLogStore.Write($"Hotkey pressed: hotkey={_hotkey}, hook=0x{_hook.ToInt64():X}");
                    HotkeyDiagnosticBuffer.RecordDispatch($"Pressed invoked operationHotkey={_diagnosticName}");
                    Pressed?.Invoke();
                    if (suppress) return (IntPtr)1;
                    return CallNextHookEx(_hook, nCode, wParam, lParam);
                }
                if (_pending != null)
                    decision = "waiting-keyup";
                else if (suppress)
                    decision = "suppressed";
                RecordDecision(FormatMessage(message), key, false, null, pressedBefore, Snapshot(_pressed),
                    pendingBefore, Snapshot(_pending), suppressedBefore, Snapshot(_suppressed), null, pendingOverlaps, decision);
                if (suppress) return (IntPtr)1;
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        catch (Exception ex) { DiagnosticLogStore.Write($"Hotkey hook callback exception: hotkey={_hotkey}, type={ex.GetType().Name}, message={ex.Message}"); return CallNextHookEx(_hook, nCode, wParam, lParam); }
    }

    private void RecordDecision(string eventName, Keys key, bool injected, bool? first,
        string pressedBefore, string pressedAfter, string pendingBefore, string pendingAfter,
        string suppressedBefore, string suppressedAfter, bool? setEquals, bool? pendingOverlapsPressed,
        string decision) => HotkeyDiagnosticBuffer.Record(new HotkeyRecognitionDiagnostic(
            DateTime.UtcNow, _diagnosticName, eventName, key.ToString(), injected, first, pressedBefore,
            pressedAfter, Snapshot(_keys), pendingBefore, pendingAfter, suppressedBefore, suppressedAfter,
            setEquals, pendingOverlapsPressed, decision));

    private void ReportRejected(Keys key, string pressedBefore, string pressedAfter, string pendingBefore,
        string reason)
    {
        string signature = $"{key}|{pressedAfter}|{Snapshot(_keys)}|{pendingBefore}|{reason}";
        long now = DateTime.UtcNow.Ticks;
        if (signature == _lastRejectedSignature && now - _lastRejectedUtcTicks < TimeSpan.FromSeconds(10).Ticks)
            return;
        _lastRejectedSignature = signature;
        _lastRejectedUtcTicks = now;
        DiagnosticLogStore.Write($"HOTKEY RECOGNITION REJECTED: hook={_diagnosticName}, key={key}, pressedBefore={pressedBefore}, pressedAfter={pressedAfter}, configured={Snapshot(_keys)}, pending={pendingBefore}, reason={reason}");
        DiagnosticLogStore.Write(HotkeyDiagnosticBuffer.FormatTail(30));
    }

    // A diagnostic rejection is meaningful only once the user has actually formed the
    // configured chord and an additional retained key prevents an exact match.
    internal static bool IsTargetHotkeyRejection(IReadOnlySet<Keys> pressed,
        IReadOnlySet<Keys> configured, Keys currentKey, string decision)
    {
        if (decision != "extra-key" || !configured.Contains(currentKey) ||
            pressed.Count <= configured.Count)
            return false;
        foreach (Keys key in configured)
            if (!pressed.Contains(key))
                return false;
        return true;
    }

    private static string Snapshot(IEnumerable<Keys>? keys)
    {
        if (keys is null)
            return "null";
        var values = new List<string>();
        foreach (Keys key in keys)
            values.Add(key.ToString());
        values.Sort(StringComparer.Ordinal);
        return values.Count == 0 ? "[]" : $"[{string.Join(",", values)}]";
    }

    private static string FormatMessage(int message) => message switch
    {
        WM_KEYDOWN => "WM_KEYDOWN",
        WM_KEYUP => "WM_KEYUP",
        WM_SYSKEYDOWN => "WM_SYSKEYDOWN",
        WM_SYSKEYUP => "WM_SYSKEYUP",
        _ => $"0x{message:X}"
    };

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
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(
        IntPtr window, out uint processId);
}

