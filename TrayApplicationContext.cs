using System;
using System.Drawing;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly HotkeyService _fullHotkeyService;
    private readonly HotkeyService _lastWordHotkeyService;
    private readonly HotkeyService _selectedTextHotkeyService;
    private readonly AppSettings _settings;
    private readonly ScannerInputService _scanner;
    private readonly System.Windows.Forms.Timer _hotkeyTimer;
    private readonly System.Windows.Forms.Timer _hotkeyWatchdog;
    private readonly Control _uiHeartbeatDispatcher;
    private readonly System.Threading.Timer _uiHeartbeatTimer;
    private TextReplacementOperationType? _queuedOperation;
    private IntPtr _lastForegroundWindow;
    private bool _callbackSilenceActive;
    private DateTime _callbackSilenceStartedUtc;
    private long _uiHeartbeatLastCompletedUtcTicks;
    private long _uiHeartbeatLatencyMilliseconds = -1;
    private int _uiHeartbeatPending;
    private int _disposing;

    public TrayApplicationContext()
    {
        KeyboardLayoutService.RefreshLayouts();
        WriteStartupDiagnostic();
        _settings = AppSettings.Load();
        UiText.Language = _settings.Language;
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _scanner = new ScannerInputService(_settings);
        _appIcon = AppAssets.GetIcon();
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = AppInfo.DisplayName,
            ContextMenuStrip = BuildMenu()
        };
        HotkeyWatchdogLog.StartSession();
        _fullHotkeyService = new HotkeyService("Full") { Hotkey = _settings.FullTextHotkey };
        _lastWordHotkeyService = new HotkeyService("Insert") { Hotkey = _settings.LastWordHotkey };
        _selectedTextHotkeyService = new HotkeyService("Pause") { Hotkey = _settings.SelectedTextHotkey };
        _uiHeartbeatDispatcher = new Control();
        _uiHeartbeatDispatcher.CreateControl();
        _uiHeartbeatTimer = new System.Threading.Timer(_ => QueueUiHeartbeat(), null, 1000, 1000);
        _hotkeyTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _hotkeyWatchdog = new System.Windows.Forms.Timer { Interval = 5000 };
        _hotkeyWatchdog.Tick += (_, _) => LogHotkeyWatchdog();
        _hotkeyWatchdog.Start();
        _hotkeyTimer.Tick += (_, _) =>
        {
            _hotkeyTimer.Stop();
            if (_queuedOperation is not TextReplacementOperationType operation)
                return;
            _queuedOperation = null;
            ExecuteHotkey(operation);
        };
        _fullHotkeyService.Pressed += () => QueueHotkey(TextReplacementOperationType.FullText);
        _lastWordHotkeyService.Pressed += () => QueueHotkey(TextReplacementOperationType.LastWord);
        _selectedTextHotkeyService.Pressed += () => QueueHotkey(TextReplacementOperationType.SelectedText);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem(AppInfo.DisplayName) { Enabled = false };
        var full = new ToolStripMenuItem($"{UiText.Get("tray_full")} ({_settings.FullTextHotkey})");
        full.Click += (_, _) => RunCorrection(TextReplacementOperationType.FullText);
        var word = new ToolStripMenuItem($"{UiText.Get("tray_word")} ({_settings.LastWordHotkey})");
        word.Click += (_, _) => RunCorrection(TextReplacementOperationType.LastWord);
        var selected = new ToolStripMenuItem($"{UiText.Get("tray_selected")} ({_settings.SelectedTextHotkey})");
        selected.Click += (_, _) => RunCorrection(TextReplacementOperationType.SelectedText);
        var settings = new ToolStripMenuItem(UiText.Get("tray_settings"));
        settings.Click += (_, _) => OpenSettings();
        var exit = new ToolStripMenuItem(UiText.Get("tray_exit"));
        exit.Click += (_, _) => ExitThread();
        menu.Items.AddRange(new ToolStripItem[] { title, new ToolStripSeparator(), full, word, selected,
            new ToolStripSeparator(), settings, exit });
        return menu;
    }

    private void QueueHotkey(TextReplacementOperationType operation)
    {
        if (CorrectionWorker.IsBusy)
            return;
        _queuedOperation = operation;
        _hotkeyTimer.Stop();
        _hotkeyTimer.Start();
    }

    private void LogHotkeyWatchdog()
    {
        long fullCallbacks = _fullHotkeyService.CallbackCount;
        long insertCallbacks = _lastWordHotkeyService.CallbackCount;
        long pauseCallbacks = _selectedTextHotkeyService.CallbackCount;
        long combinedCallbacks = fullCallbacks + insertCallbacks + pauseCallbacks;
        DateTime latestCallback = LatestCallbackUtc(_fullHotkeyService, _lastWordHotkeyService,
            _selectedTextHotkeyService);
        ForegroundDiagnostic foreground = CaptureForegroundDiagnostic();
        LogForegroundChange(foreground);
        string status = "HOTKEY WATCHDOG:" + Environment.NewLine +
            $"uiThreadId={System.Threading.Thread.CurrentThread.ManagedThreadId}, " +
            $"keyboardActivityCallbacks={combinedCallbacks}, " +
            $"keyboardActivityLastUtc={FormatUtc(latestCallback, combinedCallbacks)}, " +
            $"keyboardActivityAgeMs={FormatAge(latestCallback, combinedCallbacks)}, " +
            $"foregroundWindow=0x{foreground.Window.ToInt64():X}, foregroundProcessId={foreground.ProcessId}, " +
            $"foregroundThreadId={foreground.ThreadId}, foregroundProcess={foreground.ProcessName}, " +
            $"uiHeartbeatLastCompletedUtc={FormatHeartbeatUtc()}, uiHeartbeatAgeMs={FormatHeartbeatAge()}, " +
            $"uiHeartbeatLatencyMs={Interlocked.Read(ref _uiHeartbeatLatencyMilliseconds)}, " +
            $"uiHeartbeatPending={Volatile.Read(ref _uiHeartbeatPending)}" + Environment.NewLine +
            FormatHookStatus("full", _fullHotkeyService, fullCallbacks) + Environment.NewLine +
            FormatHookStatus("insert", _lastWordHotkeyService, insertCallbacks) + Environment.NewLine +
            FormatHookStatus("pause", _selectedTextHotkeyService, pauseCallbacks);
        HotkeyWatchdogLog.WriteHealthy(status);

        if (fullCallbacks != insertCallbacks || fullCallbacks != pauseCallbacks)
        {
            HotkeyWatchdogLog.WriteHealthy(
                $"HOTKEY WATCHDOG NOTICE: callback counters differ: full={fullCallbacks}, insert={insertCallbacks}, pause={pauseCallbacks}");
        }

        long activityAge = CallbackAgeMilliseconds(latestCallback, combinedCallbacks);
        if (activityAge >= 5000 && !_callbackSilenceActive)
        {
            _callbackSilenceActive = true;
            _callbackSilenceStartedUtc = latestCallback;
            HotkeyWatchdogLog.WriteHealthy($"HOTKEY DIAGNOSTIC CALLBACK SILENCE BEGIN: keyboardActivityAgeMs={activityAge}, foregroundWindow=0x{foreground.Window.ToInt64():X}, foregroundProcessId={foreground.ProcessId}, foregroundThreadId={foreground.ThreadId}");
            DumpCallbackBuffer("CALLBACK SILENCE BEGIN");
        }
        else if (_callbackSilenceActive && activityAge < 5000)
        {
            IReadOnlyList<HotkeyCallbackDiagnostic> callbacks = HotkeyDiagnosticBuffer.Snapshot();
            HotkeyCallbackDiagnostic? firstResumed = null;
            foreach (HotkeyCallbackDiagnostic callback in callbacks)
            {
                if (callback.TimestampUtc <= _callbackSilenceStartedUtc)
                    continue;
                firstResumed = callback;
                break;
            }
            long silenceDuration = Math.Max(0,
                (long)(latestCallback - _callbackSilenceStartedUtc).TotalMilliseconds);
            HotkeyWatchdogLog.WriteHealthy($"HOTKEY DIAGNOSTIC CALLBACK SILENCE END: silenceDurationMs={silenceDuration}, {FormatResumedCallback(firstResumed)}, foregroundWindow=0x{foreground.Window.ToInt64():X}, foregroundProcessId={foreground.ProcessId}, foregroundThreadId={foreground.ThreadId}");
            DumpCallbackBuffer("CALLBACK SILENCE END");
            _callbackSilenceActive = false;
        }
    }

    private void QueueUiHeartbeat()
    {
        if (Volatile.Read(ref _disposing) != 0 ||
            Interlocked.CompareExchange(ref _uiHeartbeatPending, 1, 0) != 0)
            return;
        long queuedAt = Stopwatch.GetTimestamp();
        try
        {
            if (_uiHeartbeatDispatcher.IsDisposed || !_uiHeartbeatDispatcher.IsHandleCreated)
            {
                Interlocked.Exchange(ref _uiHeartbeatPending, 0);
                return;
            }
            _uiHeartbeatDispatcher.BeginInvoke((MethodInvoker)(() => CompleteUiHeartbeat(queuedAt)));
        }
        catch
        {
            Interlocked.Exchange(ref _uiHeartbeatPending, 0);
        }
    }

    private void CompleteUiHeartbeat(long queuedAt)
    {
        long elapsedMilliseconds = (Stopwatch.GetTimestamp() - queuedAt) * 1000 /
            Stopwatch.Frequency;
        Interlocked.Exchange(ref _uiHeartbeatLatencyMilliseconds, elapsedMilliseconds);
        Interlocked.Exchange(ref _uiHeartbeatLastCompletedUtcTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _uiHeartbeatPending, 0);
    }

    private void LogForegroundChange(ForegroundDiagnostic foreground)
    {
        if (foreground.Window == _lastForegroundWindow)
            return;
        IntPtr oldWindow = _lastForegroundWindow;
        _lastForegroundWindow = foreground.Window;
        HotkeyWatchdogLog.WriteHealthy($"HOTKEY DIAGNOSTIC FOREGROUND CHANGED: oldHwnd=0x{oldWindow.ToInt64():X}, newHwnd=0x{foreground.Window.ToInt64():X}, newPid={foreground.ProcessId}, newThreadId={foreground.ThreadId}, process={foreground.ProcessName}");
    }

    private static ForegroundDiagnostic CaptureForegroundDiagnostic()
    {
        IntPtr window = TextReplacementService.ForegroundWindow;
        uint processId = 0;
        uint threadId = window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out processId);
        string processName = "unknown";
        if (processId != 0)
        {
            try { using Process process = Process.GetProcessById((int)processId); processName = process.ProcessName; }
            catch { }
        }
        return new ForegroundDiagnostic(window, processId, threadId, processName);
    }

    private static void DumpCallbackBuffer(string reason)
    {
        IReadOnlyList<HotkeyCallbackDiagnostic> callbacks = HotkeyDiagnosticBuffer.Snapshot();
        var lines = new List<string> { $"HOTKEY DIAGNOSTIC CALLBACK BUFFER: reason={reason}, count={callbacks.Count}" };
        foreach (HotkeyCallbackDiagnostic callback in callbacks)
        {
            lines.Add($"timestampUtc={callback.TimestampUtc:O}, hook={callback.HookName}, vk=0x{callback.VirtualKey:X2}, message={FormatMessage(callback.Message)}, flags=0x{callback.Flags:X2}, injected={callback.Injected}, callbackManagedThreadId={callback.CallbackManagedThreadId}, callbackOsThreadId={callback.CallbackOsThreadId}, foregroundWindow=0x{callback.ForegroundWindow.ToInt64():X}, foregroundThreadId={callback.ForegroundThreadId}, foregroundProcessId={callback.ForegroundProcessId}");
        }
        HotkeyWatchdogLog.WriteHealthy(string.Join(Environment.NewLine, lines));
    }

    private string FormatHeartbeatUtc()
    {
        long ticks = Interlocked.Read(ref _uiHeartbeatLastCompletedUtcTicks);
        return ticks == 0 ? "never" : new DateTime(ticks, DateTimeKind.Utc).ToString("O");
    }

    private string FormatHeartbeatAge()
    {
        long ticks = Interlocked.Read(ref _uiHeartbeatLastCompletedUtcTicks);
        return ticks == 0 ? "never" : Math.Max(0, (long)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds).ToString();
    }

    private static long CallbackAgeMilliseconds(DateTime timestamp, long callbacks) =>
        callbacks == 0 ? -1 : Math.Max(0, (long)(DateTime.UtcNow - timestamp).TotalMilliseconds);

    private static string FormatResumedCallback(HotkeyCallbackDiagnostic? callback) =>
        callback is not HotkeyCallbackDiagnostic value ? "firstResumedHook=unknown" :
        $"firstResumedHook={value.HookName}, firstResumedVk=0x{value.VirtualKey:X2}, firstResumedMessage={FormatMessage(value.Message)}, firstResumedFlags=0x{value.Flags:X2}, firstResumedInjected={value.Injected}";

    private static string FormatHookStatus(string name, HotkeyService service, long callbacks) =>
        $"{name}Alive={service.IsAlive}, {name}Hook=0x{service.HookHandle.ToInt64():X}, " +
        $"{name}Callbacks={callbacks}, {name}LastCallbackUtc={FormatUtc(service.LastCallbackUtc, callbacks)}, " +
        $"{name}CallbackAgeMs={FormatAge(service.LastCallbackUtc, callbacks)}, " +
        $"{name}LastVk={FormatVirtualKey(service.LastVirtualKey)}, " +
        $"{name}LastMessage={FormatMessage(service.LastMessage)}, " +
        $"{name}LastFlags=0x{service.LastFlags:X2}, {name}LastInjected={service.LastInjected}";

    private static DateTime LatestCallbackUtc(params HotkeyService[] services)
    {
        DateTime latest = DateTime.MinValue;
        foreach (HotkeyService service in services)
        {
            if (service.CallbackCount > 0 && service.LastCallbackUtc > latest)
                latest = service.LastCallbackUtc;
        }
        return latest;
    }

    private static string FormatUtc(DateTime timestamp, long callbacks) =>
        callbacks == 0 ? "never" : timestamp.ToString("O");

    private static string FormatAge(DateTime timestamp, long callbacks) =>
        callbacks == 0 ? "never" : CallbackAgeMilliseconds(timestamp, callbacks).ToString();

    private static string FormatVirtualKey(int virtualKey) =>
        virtualKey < 0 ? "never" : $"0x{virtualKey:X2}";

    private static string FormatMessage(int message) => message switch
    {
        0x0100 => "WM_KEYDOWN",
        0x0101 => "WM_KEYUP",
        0x0104 => "WM_SYSKEYDOWN",
        0x0105 => "WM_SYSKEYUP",
        _ => message < 0 ? "never" : $"0x{message:X}"
    };

    private void ExecuteHotkey(TextReplacementOperationType operation)
    {
        bool enabled = operation switch
        {
            TextReplacementOperationType.FullText => _settings.FullTextEnabled,
            TextReplacementOperationType.LastWord => _settings.LastWordEnabled,
            TextReplacementOperationType.SelectedText => _settings.SelectedTextEnabled,
            _ => false
        };
        if (!CorrectionWorker.IsBusy && enabled)
            RunCorrection(operation);
    }

    private static void RunCorrection(TextReplacementOperationType operation)
    {
        IntPtr target = TextReplacementService.ForegroundWindow;
        if (operation == TextReplacementOperationType.LastWord)
            DiagnosticLogStore.Write($"LastWord hotkey target capture: foreground=0x{target.ToInt64():X}");
        CorrectionWorker.TryRun(() => TextReplacementService.TryReplaceText(target, operation));
    }

    private void OpenSettings()
    {
        using var form = new SettingsShellForm(_settings, _scanner);
        if (form.ShowDialog() == DialogResult.OK)
        {
            UiText.Language = _settings.Language;
            _fullHotkeyService.Hotkey = _settings.FullTextHotkey;
            _lastWordHotkeyService.Hotkey = _settings.LastWordHotkey;
            _selectedTextHotkeyService.Hotkey = _settings.SelectedTextHotkey;
            _scanner.ApplySettings(_settings);
            _tray.ContextMenuStrip = BuildMenu();
        }
    }

    protected override void ExitThreadCore()
    {
        Volatile.Write(ref _disposing, 1);
        _uiHeartbeatTimer.Dispose();
        _uiHeartbeatDispatcher.Dispose();
        _hotkeyTimer.Stop();
        _hotkeyTimer.Dispose();
        _hotkeyWatchdog.Stop();
        _hotkeyWatchdog.Dispose();
        _fullHotkeyService.Dispose();
        _lastWordHotkeyService.Dispose();
        _selectedTextHotkeyService.Dispose();
        _scanner.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }

    private static void WriteStartupDiagnostic()
    {
        try
        {
            string available = string.Join(",", KeyboardLayoutService.Layouts.Select(layout => layout.ShortName));
            System.IO.Directory.CreateDirectory(AppRuntime.DataDirectory);
            System.IO.File.AppendAllText(AppRuntime.GetDataPath("diagnostic.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {AppInfo.DisplayName} started. Installed supported layouts: [{available}]\r\n");
        }
        catch { }
    }

    private readonly record struct ForegroundDiagnostic(IntPtr Window, uint ProcessId,
        uint ThreadId, string ProcessName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

