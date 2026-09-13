using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly KeyboardHook _hook;
    private readonly AppSettings _settings;
    private bool _processing;
    private readonly System.Windows.Forms.Timer _hotkeyTimer;
    private HotkeyAction? _queuedHotkeyAction;

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

        _hook = new KeyboardHook
        {
            FullTextHotkey = _settings.FullTextHotkey,
            LastWordHotkey = _settings.LastWordHotkey
        };

        _hotkeyTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _hotkeyTimer.Tick += (_, _) =>
        {
            _hotkeyTimer.Stop();

            if (_queuedHotkeyAction is not HotkeyAction action)
                return;

            _queuedHotkeyAction = null;
            ExecuteHotkey(action);
        };

        _hook.HotkeyPressed += OnHotkey;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var title = new ToolStripMenuItem(AppInfo.DisplayName)
        {
            Enabled = false
        };

        var full = new ToolStripMenuItem(
            $"{UiText.Get("tray_full")} ({_settings.FullTextHotkey})");
        full.Click += (_, _) => Fix(false);

        var word = new ToolStripMenuItem(
            $"{UiText.Get("tray_word")} ({_settings.LastWordHotkey})");
        word.Click += (_, _) => Fix(true);

        var settings = new ToolStripMenuItem(UiText.Get("tray_settings"));
        settings.Click += (_, _) => OpenSettings();

        var exit = new ToolStripMenuItem(UiText.Get("tray_exit"));
        exit.Click += (_, _) => ExitThread();

        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(full);
        menu.Items.Add(word);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(exit);

        return menu;
    }

    private void OnHotkey(HotkeyAction action)
    {
        if (_processing)
            return;

        // Let the final physical modifier key-up reach the foreground app
        // before we synthesize Ctrl+A/C/V. The timer runs on the WinForms
        // UI/STA thread, which is also required for Clipboard access.
        _queuedHotkeyAction = action;
        _hotkeyTimer.Stop();
        _hotkeyTimer.Start();
    }

    private void ExecuteHotkey(HotkeyAction action)
    {
        if (_processing)
            return;

        if (action == HotkeyAction.FullText && _settings.FullTextEnabled)
            Fix(false);

        if (action == HotkeyAction.LastWord && _settings.LastWordEnabled)
            Fix(true);
    }

    private void Fix(bool lastWord)
    {
        _processing = true;

        try
        {
            SelectionPreserver.SelectionSnapshot? selectionSnapshot =
                lastWord && _settings.KeepSelectionAfterCorrection
                    ? SelectionPreserver.CaptureSelection()
                    : null;

            if (TextFixer.TryFix(
                lastWord,
                out KeyboardLanguage from,
                out KeyboardLanguage to))
            {
                // Restore only a selection that existed before correction.
                // Reusing the captured UIA range is usually immediate; controls
                // that invalidate it fall back to a short caret-based restore.
                if (selectionSnapshot?.HasSelection == true)
                    SelectionPreserver.RestoreSelection(selectionSnapshot);

                // No balloon notification: the correction itself is the feedback.
            }
        }
        finally
        {
            _processing = false;
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);

        if (form.ShowDialog() == DialogResult.OK)
        {
            UiText.Language = _settings.Language;

            _hook.FullTextHotkey =
                _settings.FullTextHotkey;

            _hook.LastWordHotkey =
                _settings.LastWordHotkey;

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
            string available = string.Join(
                ",",
                KeyboardLayout.AvailableLanguages.Select(KeyboardLayout.ShortName));

            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LayoutFixer");

            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "diagnostic.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {AppInfo.DisplayName} started. Installed supported layouts: [{available}]\r\n");
        }
        catch { }
    }
}
