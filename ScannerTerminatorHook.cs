using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutFixer;

internal sealed class ScannerTerminatorHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;

    private readonly LowLevelKeyboardProc _callback;
    private readonly IntPtr _hook;
    private readonly ScannerInputService _scanner;
    private readonly HashSet<Keys> _suppressedScannerControls = new();
    private Keys? _suppressedTerminator;

    public ScannerTerminatorHook(ScannerInputService scanner)
    {
        _scanner = scanner;
        _callback = HookCallback;

        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;

        _hook = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _callback,
            GetModuleHandle(module?.ModuleName),
            0);

        if (_hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        ScannerDiagnosticLog.Write("Scanner terminator hook installed.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        int message = wParam.ToInt32();
        bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool up = message is WM_KEYUP or WM_SYSKEYUP;
        if (!down && !up)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        Keys key = (Keys)data.vkCode;

        if (key is Keys.Enter or Keys.Tab)
        {
            if (down)
            {
                if (_suppressedTerminator == key)
                    return (IntPtr)1;

                if (_scanner.TryConsumeTerminator(key))
                {
                    _suppressedTerminator = key;
                    ScannerDiagnosticLog.Write($"Physical scanner terminator suppressed: {key}");
                    return (IntPtr)1;
                }
            }
            else if (up && _suppressedTerminator == key)
            {
                _suppressedTerminator = null;
                return (IntPtr)1;
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // Some keyboard-wedge scanners (notably Honeywell Voyager configurations)
        // can emit a navigation key between two chunks of one barcode. Raw Input tells
        // ScannerInputService that a scanner terminator is currently only a candidate;
        // during that tiny continuation window we suppress the matching control key so
        // it cannot move the caret before the rest of the barcode arrives.
        if (down)
        {
            if (_suppressedScannerControls.Contains(key))
                return (IntPtr)1;

            if (_scanner.TryConsumeScannerControlKey(key))
            {
                _suppressedScannerControls.Add(key);
                ScannerDiagnosticLog.Write($"Scanner continuation control suppressed: {key}");
                return (IntPtr)1;
            }
        }
        else if (up && _suppressedScannerControls.Remove(key))
        {
            return (IntPtr)1;
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);

        ScannerDiagnosticLog.Write("Scanner terminator hook removed.");
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
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
