using System;
using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class LogViewerForm : Form
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox[] _text = new TextBox[2];
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    public LogViewerForm()
    {
        Text = UiText.Get("open_log");
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(600, 360);
        Font = new Font("Segoe UI", 10);
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            var tab = new TabPage(i == 0 ? "diagnostic.log" : "scanner_diagnostic.log") { Padding = new Padding(8) };
            var clear = new Button { Text = UiText.Get("clear_log"), Dock = DockStyle.Bottom, Height = 38 };
            _text[i] = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both, WordWrap = false, MaxLength = 0,
                Font = new Font("Consolas", 10), RightToLeft = RightToLeft.No };
            clear.Click += (_, _) =>
            {
                try { DiagnosticLogStore.Clear(index == 1); RefreshLog(index); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.Get("clear_log_failed")); }
            };
            tab.Controls.Add(_text[i]);
            tab.Controls.Add(clear);
            _tabs.TabPages.Add(tab);
        }
        Controls.Add(_tabs);
        _tabs.SelectedIndexChanged += (_, _) => RefreshLog(_tabs.SelectedIndex);
        _refresh.Tick += (_, _) => RefreshLog(_tabs.SelectedIndex);
        Shown += (_, _) => { RefreshLog(0); _refresh.Start(); };
    }
    private void RefreshLog(int index)
    {
        if (index < 0) return;
        string content;
        try { content = DiagnosticLogStore.Read(index == 1); }
        catch (Exception ex) { content = UiText.Get("log_read_failed") + "\r\n" + ex.Message; }
        var box = _text[index];
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
