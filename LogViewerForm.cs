using System;
using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class LogViewerForm : Form
{
    private readonly TextBox _text = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    public LogViewerForm()
    {
        Text = UiText.Get("open_log");
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(600, 360);
        Font = new Font("Segoe UI", 10);
        var clear = new Button { Text = UiText.Get("clear_log"), Dock = DockStyle.Bottom, Height = 38 };
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.Dock = DockStyle.Fill;
        _text.ScrollBars = ScrollBars.Both;
        _text.WordWrap = false;
        _text.MaxLength = 0;
        _text.Font = new Font("Consolas", 10);
        _text.RightToLeft = RightToLeft.No;
        clear.Click += (_, _) =>
        {
            try { DiagnosticLogStore.Clear(); RefreshLog(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.Get("clear_log_failed")); }
        };
        Controls.Add(_text);
        Controls.Add(clear);
        _refresh.Tick += (_, _) => RefreshLog();
        Shown += (_, _) => { RefreshLog(); _refresh.Start(); };
    }
    private void RefreshLog()
    {
        string content;
        try { content = DiagnosticLogStore.Read(); }
        catch (Exception ex) { content = UiText.Get("log_read_failed") + "\r\n" + ex.Message; }
        var box = _text;
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
