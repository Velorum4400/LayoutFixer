using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly KeyboardHook _hook;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _hotkeyTimer;
    private bool _hotkeyQueued;

    public TrayApplicationContext()
    {
        KeyboardLayout.Initialize();
        WriteStartupDiagnostic();
        _settings = AppSettings.Load();
        UiText.Language = _settings.Language;
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _appIcon = AppAssets.GetIcon();
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = AppInfo.DisplayName,
            ContextMenuStrip = BuildMenu()
        };
        _hook = new KeyboardHook { FullTextHotkey = _settings.FullTextHotkey };
        _hotkeyTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _hotkeyTimer.Tick += (_, _) =>
        {
            _hotkeyTimer.Stop();
            if (!_hotkeyQueued)
                return;
            _hotkeyQueued = false;
            ExecuteFullTextHotkey();
        };
        _hook.HotkeyPressed += OnHotkey;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem(AppInfo.DisplayName) { Enabled = false };
        var full = new ToolStripMenuItem($"{UiText.Get("tray_full")} ({_settings.FullTextHotkey})");
        full.Click += (_, _) => FixAllText();
        var word = new ToolStripMenuItem($"{UiText.Get("tray_word")} — {UiText.Get("temporarily_unavailable")}")
        {
            Enabled = false
        };
        var settings = new ToolStripMenuItem(UiText.Get("tray_settings"));
        settings.Click += (_, _) => OpenSettings();
        var exit = new ToolStripMenuItem(UiText.Get("tray_exit"));
        exit.Click += (_, _) => ExitThread();
        menu.Items.AddRange(new ToolStripItem[] { title, new ToolStripSeparator(), full, word,
            new ToolStripSeparator(), settings, exit });
        return menu;
    }

    private void OnHotkey()
    {
        if (CorrectionWorker.IsBusy)
            return;
        _hotkeyQueued = true;
        _hotkeyTimer.Stop();
        _hotkeyTimer.Start();
    }

    private void ExecuteFullTextHotkey()
    {
        if (!CorrectionWorker.IsBusy && _settings.FullTextEnabled)
            FixAllText();
    }

    private void FixAllText()
    {
        IntPtr target = TextFixer.ForegroundWindow;
        CorrectionWorker.TryRun(() => TextFixer.TryFixAllText(
            out KeyboardLanguage _, out KeyboardLanguage _, target));
    }

    private void OpenSettings()
    {
        using var form = new SettingsShellForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            UiText.Language = _settings.Language;
            _hook.FullTextHotkey = _settings.FullTextHotkey;
            _tray.ContextMenuStrip = BuildMenu();
        }
    }

    protected override void ExitThreadCore()
    {
        _hotkeyTimer.Stop();
        _hotkeyTimer.Dispose();
        _hook.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }

    private static void WriteStartupDiagnostic()
    {
        try
        {
            string available = string.Join(",", KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.ShortName));
            System.IO.Directory.CreateDirectory(AppRuntime.DataDirectory);
            System.IO.File.AppendAllText(AppRuntime.GetDataPath("diagnostic.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {AppInfo.DisplayName} started. Installed supported layouts: [{available}]\r\n");
        }
        catch { }
    }
}
