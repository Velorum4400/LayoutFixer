using System.Drawing;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class HotkeyEditorForm : Form
{
    private readonly HotkeyCaptureBox _capture;

    public string Hotkey => _capture.Hotkey;

    public HotkeyEditorForm(string language)
    {
        UiText.Language = language;

        Text = UiText.Get("new_hotkey");
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 235);
        BackColor = Color.FromArgb(246, 249, 253);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = UiText.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = UiText.IsRtl;

        var title = new Label
        {
            Text = UiText.Get("new_hotkey"),
            Left = 28,
            Top = 24,
            Width = 414,
            Height = 34,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 40, 70)
        };

        var label = new Label
        {
            Text = UiText.Get("press_hotkey"),
            Left = 30,
            Top = 66,
            Width = 410,
            Height = 28,
            ForeColor = Color.FromArgb(88, 101, 122)
        };

        _capture = new HotkeyCaptureBox
        {
            Left = 30,
            Top = 100,
            Width = 410,
            Height = 34,
            Hotkey = ""
        };

        var cancel = new ModernButton
        {
            Text = UiText.Get("cancel"),
            Left = 236,
            Top = 166,
            Width = 96,
            Height = 42,
            Primary = false,
            DialogResult = DialogResult.Cancel
        };

        var ok = new ModernButton
        {
            Text = UiText.Get("save"),
            Left = 342,
            Top = 166,
            Width = 98,
            Height = 42,
            Primary = true,
            DialogResult = DialogResult.None
        };

        ok.Click += (_, _) =>
        {
            if (!HotkeyDefinition.IsValid(_capture.Hotkey))
            {
                MessageBox.Show(
                    this,
                    UiText.Get("press_first"),
                    AppInfo.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                _capture.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[]
        {
            title,
            label,
            _capture,
            cancel,
            ok
        });

        CancelButton = cancel;
        Shown += (_, _) => _capture.Focus();
    }
}
