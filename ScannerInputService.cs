using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class ScannerDeviceInfo
{
    public string DevicePath { get; init; } = "";
    public string VendorId { get; init; } = "";
    public string ProductId { get; init; } = "";

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(VendorId) || !string.IsNullOrWhiteSpace(ProductId)
            ? $"HID keyboard — VID_{VendorId} / PID_{ProductId}"
            : "HID keyboard device";
}

public sealed class ScannerInputService : IDisposable
{
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const int WM_INPUT = 0x00FF;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // A scanner Enter/Tab is treated as a candidate suffix first. This is important
    // for some Honeywell keyboard-wedge configurations that can emit Enter, then a
    // navigation key, then continue sending the same barcode.
    private const int TerminatorDecisionDelayMs = 110;
    private const int ScannerControlWindowMs = 180;

    private readonly RawInputWindow _window;
    private readonly Dictionary<IntPtr, ScannerDeviceInfo> _deviceCache = new();
    private readonly StringBuilder _runtimeBuffer = new();
    private readonly StringBuilder _identifyBuffer = new();
    private readonly System.Windows.Forms.Timer _completionTimer;
    private readonly System.Windows.Forms.Timer _terminatorTimer;

    private AppSettings _settings;
    private IntPtr _identifyDevice;
    private DateTime _lastRuntimeInputUtc;
    private DateTime _lastIdentifyInputUtc;
    private DateTime _pendingTerminatorUtc;
    private bool _runtimeShift;
    private bool _identifyShift;
    private Keys? _pendingSuffix;
    private int _terminatorGeneration;

    public bool IdentificationActive { get; private set; }
    public event Action<ScannerDeviceInfo, string>? ScannerIdentified;
    public event Action<string>? IdentificationProgress;

    public ScannerInputService(AppSettings settings)
    {
        _settings = settings;
        ScannerDiagnosticLog.Write(
            $"Scanner service starting. enabled={settings.ScannerEnabled}, device='{settings.ScannerDisplayName}', VID={settings.ScannerVendorId}, PID={settings.ScannerProductId}, path='{settings.ScannerDevicePath}'");

        try
        {
            _window = new RawInputWindow(this);
        }
        catch (Exception ex)
        {
            ScannerDiagnosticLog.WriteException("Raw Input registration failed", ex);
            throw;
        }

        _completionTimer = new System.Windows.Forms.Timer { Interval = 140 };
        _completionTimer.Tick += (_, _) =>
        {
            _completionTimer.Stop();

            // If a candidate Enter/Tab is pending, let the terminator timer decide.
            if (_pendingSuffix != null)
                return;

            if (_runtimeBuffer.Length >= Math.Max(1, _settings.ScannerMinimumLength))
            {
                ScannerDiagnosticLog.Write($"Runtime scan completed by timeout. length={_runtimeBuffer.Length}");
                CompleteRuntimeScan(null);
            }
            else
            {
                if (_runtimeBuffer.Length > 0)
                    ScannerDiagnosticLog.Write($"Runtime buffer discarded by timeout. length={_runtimeBuffer.Length}");
                ResetRuntimeBuffer();
            }
        };

        _terminatorTimer = new System.Windows.Forms.Timer { Interval = TerminatorDecisionDelayMs };
        _terminatorTimer.Tick += (_, _) =>
        {
            _terminatorTimer.Stop();

            if (_pendingSuffix == null)
                return;

            Keys suffix = _pendingSuffix.Value;
            _pendingSuffix = null;

            if (_runtimeBuffer.Length < Math.Max(1, _settings.ScannerMinimumLength))
            {
                ScannerDiagnosticLog.Write("Scanner terminator candidate expired but runtime buffer was too short.");
                ResetRuntimeBuffer();
                return;
            }

            ScannerDiagnosticLog.Write(
                $"Scanner terminator confirmed after {TerminatorDecisionDelayMs} ms with no continuation. suffix={suffix}, length={_runtimeBuffer.Length}");
            CompleteRuntimeScan(suffix);
        };
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        ResetRuntimeBuffer();
        CancelPendingTerminator();
        ScannerDiagnosticLog.Write(
            $"Scanner settings applied. enabled={settings.ScannerEnabled}, device='{settings.ScannerDisplayName}', VID={settings.ScannerVendorId}, PID={settings.ScannerProductId}, path='{settings.ScannerDevicePath}'");
    }

    public void BeginIdentification()
    {
        CancelPendingTerminator();
        IdentificationActive = true;
        _identifyDevice = IntPtr.Zero;
        _identifyBuffer.Clear();
        _identifyShift = false;
        _lastIdentifyInputUtc = DateTime.MinValue;
        ScannerDiagnosticLog.Write("Scanner identification started. Waiting for a barcode from one HID keyboard device.");
        IdentificationProgress?.Invoke("");
    }

    public void CancelIdentification()
    {
        if (IdentificationActive)
            ScannerDiagnosticLog.Write("Scanner identification cancelled.");

        IdentificationActive = false;
        _identifyDevice = IntPtr.Zero;
        _identifyBuffer.Clear();
        _identifyShift = false;
    }

    public bool TryConsumeTerminator(Keys key)
    {
        if (!_settings.ScannerEnabled || IdentificationActive || key is not (Keys.Enter or Keys.Tab))
            return false;

        if (_runtimeBuffer.Length < Math.Max(1, _settings.ScannerMinimumLength))
            return false;

        double ageMs = (DateTime.UtcNow - _lastRuntimeInputUtc).TotalMilliseconds;
        if (ageMs > 350)
            return false;

        _completionTimer.Stop();
        _pendingSuffix = key;
        _pendingTerminatorUtc = DateTime.UtcNow;
        int generation = ++_terminatorGeneration;

        ScannerDiagnosticLog.Write(
            $"Runtime terminator correlated in low-level hook. key={key}, ageMs={ageMs:F1}, text='{Sample(_runtimeBuffer.ToString())}', typedLength={_runtimeBuffer.Length}; waiting for possible continuation.");

        // Never wait in the low-level hook. Schedule the decision on the WinForms UI thread.
        _window.Post(() =>
        {
            if (_pendingSuffix != key || generation != _terminatorGeneration)
                return;

            _terminatorTimer.Stop();
            _terminatorTimer.Start();
            ScannerDiagnosticLog.Write(
                $"Scanner terminator candidate scheduled outside keyboard hook. delay={TerminatorDecisionDelayMs} ms");
        });

        return true;
    }

    public bool TryConsumeScannerControlKey(Keys key)
    {
        if (!_settings.ScannerEnabled || IdentificationActive || _pendingSuffix == null)
            return false;

        // Honeywell Voyager 1250g has been observed to emit VK_DOWN between two chunks
        // of one scan. Suppress only this known navigation key, and only immediately
        // after a scanner terminator candidate, to avoid interfering with normal typing.
        if (key != Keys.Down)
            return false;

        double ageMs = (DateTime.UtcNow - _pendingTerminatorUtc).TotalMilliseconds;
        if (ageMs < 0 || ageMs > ScannerControlWindowMs)
            return false;

        ScannerDiagnosticLog.Write(
            $"Scanner continuation control correlated. key={key}, ageSinceTerminatorMs={ageMs:F1}");
        return true;
    }

    private void ProcessRawInput(IntPtr rawInputHandle)
    {
        try
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            GetRawInputData(rawInputHandle, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0)
                return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(rawInputHandle, RID_INPUT, buffer, ref size, headerSize) != size)
                    return;

                RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
                if (raw.header.dwType != RIM_TYPEKEYBOARD || raw.header.hDevice == IntPtr.Zero)
                    return;

                int message = unchecked((int)raw.keyboard.Message);
                bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool up = message is WM_KEYUP or WM_SYSKEYUP;
                if (!down && !up)
                    return;

                ushort vk = raw.keyboard.VKey;
                ScannerDeviceInfo device = GetDeviceInfo(raw.header.hDevice);

                if (IdentificationActive)
                    ProcessIdentification(raw.header.hDevice, device, vk, down, up);

                if (!IdentificationActive && _settings.ScannerEnabled && MatchesSelectedScanner(device))
                    ProcessRuntime(vk, down, up);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            ScannerDiagnosticLog.WriteException("ProcessRawInput failed", ex);
        }
    }

    private void ProcessIdentification(IntPtr hDevice, ScannerDeviceInfo device, ushort vk, bool down, bool up)
    {
        DateTime now = DateTime.UtcNow;
        if (_identifyDevice != hDevice || (now - _lastIdentifyInputUtc).TotalMilliseconds > 500)
        {
            _identifyDevice = hDevice;
            _identifyBuffer.Clear();
            _identifyShift = false;
            ScannerDiagnosticLog.Write($"Identification input source: {device.DisplayName}; path='{device.DevicePath}'");
        }

        _lastIdentifyInputUtc = now;
        if (IsShift(vk))
        {
            _identifyShift = down;
            return;
        }

        if (!down)
            return;

        if (vk is 0x0D or 0x09)
        {
            if (_identifyBuffer.Length >= 3)
            {
                string barcode = _identifyBuffer.ToString();
                IdentificationActive = false;
                ScannerDiagnosticLog.Write(
                    $"Scanner identified successfully: {device.DisplayName}; barcode='{Sample(barcode)}'; path='{device.DevicePath}'");
                ScannerIdentified?.Invoke(device, barcode);
            }
            else
            {
                ScannerDiagnosticLog.Write(
                    $"Identification terminator received but barcode was too short. length={_identifyBuffer.Length}");
            }
            return;
        }

        if (TryMapUsKey(vk, _identifyShift, out char c))
        {
            _identifyBuffer.Append(c);
            IdentificationProgress?.Invoke(_identifyBuffer.ToString());
        }
        else
        {
            ScannerDiagnosticLog.Write(
                $"Identification ignored unmapped key. VK=0x{vk:X2}, shift={_identifyShift}");
        }
    }

    private void ProcessRuntime(ushort vk, bool down, bool up)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastRuntimeInputUtc).TotalMilliseconds > 500)
        {
            if (_runtimeBuffer.Length > 0)
                ScannerDiagnosticLog.Write($"Runtime buffer reset after input gap. oldLength={_runtimeBuffer.Length}");
            ResetRuntimeBuffer();
            CancelPendingTerminator();
        }

        _lastRuntimeInputUtc = now;

        if (IsShift(vk))
        {
            _runtimeShift = down;
            return;
        }

        if (!down)
            return;

        if (vk is 0x0D or 0x09)
        {
            if (_runtimeBuffer.Length >= Math.Max(1, _settings.ScannerMinimumLength))
            {
                ScannerDiagnosticLog.Write(
                    $"Raw Input terminator observed; treating as candidate suffix. length={_runtimeBuffer.Length}");
            }
            return;
        }

        if (TryMapUsKey(vk, _runtimeShift, out char c))
        {
            if (_pendingSuffix != null)
            {
                Keys previousCandidate = _pendingSuffix.Value;
                double gapMs = (now - _pendingTerminatorUtc).TotalMilliseconds;
                _pendingSuffix = null;
                _terminatorGeneration++;
                _terminatorTimer.Stop();

                ScannerDiagnosticLog.Write(
                    $"Scanner continued after candidate terminator. previous={previousCandidate}, gapMs={gapMs:F1}, nextChar='{c}'. Keeping one barcode buffer.");
            }

            _runtimeBuffer.Append(c);
            _completionTimer.Stop();
            _completionTimer.Start();
        }
        else
        {
            // Do not cancel a candidate suffix for known non-text continuation controls.
            ScannerDiagnosticLog.Write(
                $"Runtime ignored unmapped key. VK=0x{vk:X2}, shift={_runtimeShift}, pendingSuffix={_pendingSuffix?.ToString() ?? "None"}");
        }
    }

    private void CompleteRuntimeScan(Keys? suffix)
    {
        if (_runtimeBuffer.Length == 0)
            return;

        string text = _runtimeBuffer.ToString();
        int typedLength = _runtimeBuffer.Length;

        _completionTimer.Stop();
        _terminatorTimer.Stop();
        _runtimeBuffer.Clear();
        _runtimeShift = false;
        _pendingSuffix = null;
        _terminatorGeneration++;

        ScannerDiagnosticLog.Write(
            $"Runtime barcode ready: text='{Sample(text)}', typedLength={typedLength}, suffix={suffix?.ToString() ?? "None"}");
        IntPtr target = TextFixer.ForegroundWindow;
        _window.Post(() =>
        {
            if (!CorrectionWorker.TryRun(() =>
            {
                if (TextFixer.ForegroundWindow == target)
                    ScannerTextInjector.ReplacePreviousText(text, typedLength, suffix);
                else
                    ScannerDiagnosticLog.Write("Scanner correction skipped: focus changed.");
            }))
                ScannerDiagnosticLog.Write("Scanner correction skipped: another correction is active.");
        });
    }

    private void ResetRuntimeBuffer()
    {
        _completionTimer.Stop();
        _runtimeBuffer.Clear();
        _runtimeShift = false;
    }

    private void CancelPendingTerminator()
    {
        _terminatorTimer.Stop();
        _pendingSuffix = null;
        _terminatorGeneration++;
        _pendingTerminatorUtc = DateTime.MinValue;
    }

    private bool MatchesSelectedScanner(ScannerDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ScannerDevicePath) &&
            string.Equals(device.DevicePath, _settings.ScannerDevicePath, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(_settings.ScannerVendorId) &&
               !string.IsNullOrWhiteSpace(_settings.ScannerProductId) &&
               string.Equals(device.VendorId, _settings.ScannerVendorId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(device.ProductId, _settings.ScannerProductId, StringComparison.OrdinalIgnoreCase);
    }

    private ScannerDeviceInfo GetDeviceInfo(IntPtr hDevice)
    {
        if (_deviceCache.TryGetValue(hDevice, out ScannerDeviceInfo? cached))
            return cached;

        string path = GetDevicePath(hDevice);
        Match match = Regex.Match(path, @"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);

        var info = new ScannerDeviceInfo
        {
            DevicePath = path,
            VendorId = match.Success ? match.Groups[1].Value.ToUpperInvariant() : "",
            ProductId = match.Success ? match.Groups[2].Value.ToUpperInvariant() : ""
        };

        _deviceCache[hDevice] = info;
        ScannerDiagnosticLog.Write(
            $"Discovered HID keyboard device: {info.DisplayName}; path='{info.DevicePath}'");
        return info;
    }

    private static string GetDevicePath(IntPtr hDevice)
    {
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
            return "";

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size * 2));
        try
        {
            uint chars = size;
            uint result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref chars);
            if (result == uint.MaxValue)
                return "";
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsShift(ushort vk) => vk is 0x10 or 0xA0 or 0xA1;

    private static bool TryMapUsKey(ushort vk, bool shift, out char value)
    {
        value = '\0';

        if (vk is >= 0x41 and <= 0x5A)
        {
            char c = (char)vk;
            value = shift ? c : char.ToLowerInvariant(c);
            return true;
        }

        if (vk is >= 0x30 and <= 0x39)
        {
            const string normal = "0123456789";
            const string shifted = ")!@#$%^&*(";
            int index = vk - 0x30;
            value = shift ? shifted[index] : normal[index];
            return true;
        }

        if (vk is >= 0x60 and <= 0x69)
        {
            value = (char)('0' + (vk - 0x60));
            return true;
        }

        value = vk switch
        {
            0x20 => ' ',
            0x6A => '*',
            0x6B => '+',
            0x6D => '-',
            0x6E => '.',
            0x6F => '/',
            0xBA => shift ? ':' : ';',
            0xBB => shift ? '+' : '=',
            0xBC => shift ? '<' : ',',
            0xBD => shift ? '_' : '-',
            0xBE => shift ? '>' : '.',
            0xBF => shift ? '?' : '/',
            0xC0 => shift ? '~' : '`',
            0xDB => shift ? '{' : '[',
            0xDC => shift ? '|' : '\\',
            0xDD => shift ? '}' : ']',
            0xDE => shift ? '"' : '\'',
            _ => '\0'
        };

        return value != '\0';
    }

    private static string Sample(string value)
    {
        string text = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return text.Length <= 100 ? text : text[..100] + "...";
    }

    public void Dispose()
    {
        ScannerDiagnosticLog.Write("Scanner service stopping.");
        _completionTimer.Stop();
        _completionTimer.Dispose();
        _terminatorTimer.Stop();
        _terminatorTimer.Dispose();
        _window.Dispose();
    }

    private sealed class RawInputWindow : NativeWindow, IDisposable
    {
        private readonly ScannerInputService _owner;
        private readonly Control _dispatcher = new();

        public RawInputWindow(ScannerInputService owner)
        {
            _owner = owner;
            _dispatcher.CreateControl();
            CreateHandle(new CreateParams
            {
                Caption = "LayoutFixer.RawInput",
                Parent = new IntPtr(-3)
            });

            RAWINPUTDEVICE[] devices =
            {
                new()
                {
                    usUsagePage = 0x01,
                    usUsage = 0x06,
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = Handle
                }
            };

            if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            ScannerDiagnosticLog.Write(
                $"Raw Input keyboard registration succeeded. hwnd=0x{Handle.ToInt64():X}");
        }

        public void Post(Action action)
        {
            if (_dispatcher.IsDisposed)
                return;

            try
            {
                _dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException ex)
            {
                ScannerDiagnosticLog.WriteException("Dispatcher BeginInvoke failed", ex);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
                _owner.ProcessRawInput(m.LParam);

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try
            {
                RAWINPUTDEVICE[] devices =
                {
                    new()
                    {
                        usUsagePage = 0x01,
                        usUsage = 0x06,
                        dwFlags = RIDEV_REMOVE,
                        hwndTarget = IntPtr.Zero
                    }
                };

                RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                ScannerDiagnosticLog.Write("Raw Input keyboard registration removed.");
            }
            catch (Exception ex)
            {
                ScannerDiagnosticLog.WriteException("Raw Input unregister failed", ex);
            }

            DestroyHandle();
            _dispatcher.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWKEYBOARD keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);
}
