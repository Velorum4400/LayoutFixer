using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class ScannerDeviceInfo
{
    public string DevicePath { get; init; } = "";
    public string VendorId { get; init; } = "";
    public string ProductId { get; init; } = "";
    public string DisplayName => string.IsNullOrEmpty(VendorId) ? "HID keyboard device" : $"HID keyboard — VID_{VendorId} / PID_{ProductId}";
}

public sealed class ScannerInputService : IDisposable
{
    private const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007, RidevInputSink = 0x100;
    private const int WmInput = 0x00FF, RimTypeKeyboard = 1;
    private readonly RawWindow _window;
    private readonly Dictionary<IntPtr, ScannerDeviceInfo> _devices = new();
    private AppSettings _settings;
    private bool _identifying;
    private IntPtr _identifiedDevice;
    public event Action<ScannerDeviceInfo>? ScannerIdentified;

    public ScannerInputService(AppSettings settings) { _settings = settings; _window = new RawWindow(this); }
    public void ApplySettings(AppSettings settings) => _settings = settings;
    public void BeginIdentification() { _identifying = true; _identifiedDevice = IntPtr.Zero; ScannerDiagnosticLog.Write("Scanner identification started."); }
    public void CancelIdentification() => _identifying = false;

    private void ProcessRawInput(IntPtr handle)
    {
        var timer = Stopwatch.StartNew();
        uint size = 0, headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(handle, RidInput, IntPtr.Zero, ref size, headerSize) == 0 || size == 0) return;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(handle, RidInput, buffer, ref size, headerSize) != size) return;
            RAWINPUT input = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (input.header.dwType != RimTypeKeyboard || input.header.hDevice == IntPtr.Zero || input.keyboard.Message is not 0x100 and not 0x104) return;
            ScannerDeviceInfo device = GetDevice(input.header.hDevice);
            if (_identifying)
            {
                _identifying = false; _identifiedDevice = input.header.hDevice;
                ScannerDiagnosticLog.Write($"Scanner identified: device={device.DisplayName}, path='{device.DevicePath}'");
                ScannerIdentified?.Invoke(device); return;
            }
            if (!_settings.ScannerEnabled || !Matches(device)) return;
            IntPtr target = GetForegroundWindow();
            if (target == IntPtr.Zero || !KeyboardLayoutService.TryGetCurrentLayout(target, out KeyboardLayoutInfo current)) return;
            ScannerDiagnosticLog.Write($"Scanner RawInput detected device='{device.DevicePath}', target=0x{target.ToInt64():X}, elapsedFromRawInput={timer.ElapsedMilliseconds} ms");
            if ((current.LanguageId & 0x03ff) == 0x09) { ScannerDiagnosticLog.Write("Scanner current layout: English; switchRequired=False"); return; }
            if (!KeyboardLayoutService.TryGetEnglish(out KeyboardLayoutInfo english)) { ScannerDiagnosticLog.Write("FAIL: English keyboard layout is not available"); return; }
            if (GetForegroundWindow() != target) { ScannerDiagnosticLog.Write("Scanner switch skipped: foreground window changed"); return; }
            bool requested = KeyboardLayoutService.SwitchLayout(target, english);
            ScannerDiagnosticLog.Write($"Scanner English layout switch requested: success={requested}, elapsedFromRawInput={timer.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { ScannerDiagnosticLog.Write($"Scanner RawInput failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private bool Matches(ScannerDeviceInfo d) => !string.IsNullOrEmpty(_settings.ScannerDevicePath) ? string.Equals(d.DevicePath, _settings.ScannerDevicePath, StringComparison.OrdinalIgnoreCase) : !string.IsNullOrEmpty(_settings.ScannerVendorId) && string.Equals(d.VendorId, _settings.ScannerVendorId, StringComparison.OrdinalIgnoreCase) && string.Equals(d.ProductId, _settings.ScannerProductId, StringComparison.OrdinalIgnoreCase);
    private ScannerDeviceInfo GetDevice(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out ScannerDeviceInfo? d)) return d;
        uint chars = 0; GetRawInputDeviceInfo(handle, RidiDeviceName, IntPtr.Zero, ref chars); IntPtr text = Marshal.AllocHGlobal((int)chars * 2);
        try { GetRawInputDeviceInfo(handle, RidiDeviceName, text, ref chars); string path = Marshal.PtrToStringUni(text) ?? ""; Match m = Regex.Match(path, @"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.IgnoreCase); d = new ScannerDeviceInfo { DevicePath = path, VendorId = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "", ProductId = m.Success ? m.Groups[2].Value.ToUpperInvariant() : "" }; _devices[handle] = d; return d; } finally { Marshal.FreeHGlobal(text); }
    }
    public void Dispose() => _window.Dispose();
    private sealed class RawWindow : NativeWindow, IDisposable
    {
        private readonly ScannerInputService _owner;
        public RawWindow(ScannerInputService owner) { _owner = owner; CreateHandle(new CreateParams { Caption = "LayoutFixer.RawInput", Parent = new IntPtr(-3) }); var d = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = RidevInputSink, hwndTarget = Handle }; if (!RegisterRawInputDevices(new[] { d }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>())) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmInput) _owner.ProcessRawInput(m.LParam); base.WndProc(ref m); }
        public void Dispose() { DestroyHandle(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUT { public RAWINPUTHEADER header; public RAWKEYBOARD keyboard; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] p, uint n, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr h, uint command, IntPtr data, ref uint size, uint header);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr h, uint command, IntPtr data, ref uint size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}

