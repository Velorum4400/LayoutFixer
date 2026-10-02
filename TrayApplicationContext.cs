using System;
using System.Drawing;
using System.Linq;
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
    private TextReplacementOperationType? _queuedOperation;

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
        _fullHotkeyService = new HotkeyService { Hotkey = _settings.FullTextHotkey };
        _lastWordHotkeyService = new HotkeyService { Hotkey = _settings.LastWordHotkey };
        _selectedTextHotkeyService = new HotkeyService { Hotkey = _settings.SelectedTextHotkey };
        HotkeyWatchdogLog.StartSession();
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
        string status = "HOTKEY WATCHDOG:" + Environment.NewLine +
            $"uiThreadId={System.Threading.Thread.CurrentThread.ManagedThreadId}, " +
            $"keyboardActivityCallbacks={combinedCallbacks}, " +
            $"keyboardActivityLastUtc={FormatUtc(latestCallback, combinedCallbacks)}, " +
            $"keyboardActivityAgeMs={FormatAge(latestCallback, combinedCallbacks)}" + Environment.NewLine +
            FormatHookStatus("full", _fullHotkeyService, fullCallbacks) + Environment.NewLine +
            FormatHookStatus("insert", _lastWordHotkeyService, insertCallbacks) + Environment.NewLine +
            FormatHookStatus("pause", _selectedTextHotkeyService, pauseCallbacks);
        HotkeyWatchdogLog.WriteHealthy(status);

        if (fullCallbacks != insertCallbacks || fullCallbacks != pauseCallbacks)
        {
            HotkeyWatchdogLog.WriteHealthy(
                $"HOTKEY WATCHDOG NOTICE: callback counters differ: full={fullCallbacks}, insert={insertCallbacks}, pause={pauseCallbacks}");
        }
    }

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
        callbacks == 0 ? "never" : Math.Max(0, (long)(DateTime.UtcNow - timestamp).TotalMilliseconds).ToString();

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
}

