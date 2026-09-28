using System;
using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class LogViewerForm : Form
{
    private readonly TextBox _text = CreateLogBox();
    private readonly TextBox _scannerText = CreateLogBox();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    public LogViewerForm()
    {
        Text = UiText.Get("open_log");
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(600, 360);
        Font = new Font("Segoe UI", 10);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateTab(UiText.Get("open_log"), _text, () => DiagnosticLogStore.Clear()));
        tabs.TabPages.Add(CreateTab(UiText.Get("scanner_section"), _scannerText, () =>
        {
            try { System.IO.File.WriteAllText(ScannerDiagnosticLog.LogPath, string.Empty); } catch { }
        }));
        Controls.Add(tabs);
        _refresh.Tick += (_, _) => RefreshLog();
        Shown += (_, _) => { RefreshLog(); _refresh.Start(); };
    }
    private void RefreshLog()
    {
        RefreshBox(_text, () => DiagnosticLogStore.Read());
        RefreshBox(_scannerText, () => System.IO.File.Exists(ScannerDiagnosticLog.LogPath)
            ? System.IO.File.ReadAllText(ScannerDiagnosticLog.LogPath) : string.Empty);
    }
    private static TextBox CreateLogBox() => new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
        WordWrap = false, MaxLength = 0, Font = new Font("Consolas", 10), RightToLeft = RightToLeft.No
    };
    private TabPage CreateTab(string title, TextBox box, Action clearAction)
    {
        var page = new TabPage(title);
        var clear = new Button { Text = UiText.Get("clear_log"), Dock = DockStyle.Bottom, Height = 38 };
        clear.Click += (_, _) => { clearAction(); RefreshLog(); };
        page.Controls.Add(box); page.Controls.Add(clear); return page;
    }
    private void RefreshBox(TextBox box, Func<string> read)
    {
        string content;
        try { content = read(); }
        catch (Exception ex) { content = UiText.Get("log_read_failed") + "\r\n" + ex.Message; }
        if (box.Text == content) return;
        int start = box.SelectionStart, length = box.SelectionLength;
        bool followEnd = start + length >= box.TextLength;
        box.Text = content;
        box.Select(followEnd ? box.TextLength : Math.Min(start, box.TextLength),
            followEnd ? 0 : Math.Min(length, Math.Max(0, box.TextLength - start)));
        if (followEnd) box.ScrollToCaret();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Dispose(); Icon?.Dispose(); }
        base.Dispose(disposing);
    }
}

