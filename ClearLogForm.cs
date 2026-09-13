using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class ClearLogForm : Form
{
    private readonly Label _message;
    private readonly TextBox _details;
    private readonly ModernButton _yes;
    private readonly ModernButton _cancel;
    private readonly ModernButton _close;

    public ClearLogForm()
    {
        Text = UiText.Get("clear_log");
        Icon = AppAssets.GetIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 230);
        MinimumSize = new Size(500, 220);
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        RightToLeft = UiText.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = false;

        _message = new Label
        {
            Left = 28,
            Top = 28,
            Width = 464,
            Height = 58,
            Text = UiText.Get("clear_log_confirm"),
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            TextAlign = UiText.IsRtl ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft
        };

        _details = new TextBox
        {
            Left = 28,
            Top = 82,
            Width = 464,
            Height = 76,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(120, 40, 40),
            ScrollBars = ScrollBars.Vertical,
            Visible = false,
            RightToLeft = UiText.IsRtl ? RightToLeft.Yes : RightToLeft.No
        };

        _yes = new ModernButton
        {
            Width = 120,
            Height = 42,
            Primary = true,
            Text = UiText.Get("yes")
        };
        _yes.Click += (_, _) => ClearLog();

        _cancel = new ModernButton
        {
            Width = 120,
            Height = 42,
            Primary = false,
            Text = UiText.Get("cancel")
        };
        _cancel.Click += (_, _) => Close();

        _close = new ModernButton
        {
            Width = 120,
            Height = 42,
            Primary = true,
            Text = UiText.Get("close"),
            Visible = false
        };
        _close.Click += (_, _) => Close();

        PositionButtons();
        Controls.AddRange(new Control[] { _message, _details, _yes, _cancel, _close });
    }

    private void PositionButtons()
    {
        int y = ClientSize.Height - 62;
        _cancel.SetBounds(ClientSize.Width - 28 - _cancel.Width, y, _cancel.Width, _cancel.Height);
        _yes.SetBounds(_cancel.Left - 12 - _yes.Width, y, _yes.Width, _yes.Height);
        _close.SetBounds(ClientSize.Width - 28 - _close.Width, y, _close.Width, _close.Height);
    }

    private void ClearLog()
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LayoutFixer");
            Directory.CreateDirectory(directory);

            string diagnosticPath = Path.Combine(directory, "diagnostic.log");
            File.WriteAllText(diagnosticPath, string.Empty);

            ShowResult(UiText.Get("clear_log_success"), null, success: true);
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
            ShowResult(UiText.Get("clear_log_failed"), ex.Message, success: false);
        }
    }

    private void ShowResult(string message, string? details, bool success)
    {
        _message.Text = message;
        _message.ForeColor = success ? Color.FromArgb(25, 110, 65) : Color.FromArgb(160, 45, 45);
        _message.Height = 50;

        _yes.Visible = false;
        _cancel.Visible = false;
        _close.Visible = true;

        if (!string.IsNullOrWhiteSpace(details))
        {
            _details.Text = details;
            _details.Visible = true;
        }
        else
        {
            _details.Visible = false;
        }
    }

    private static void WriteErrorLog(Exception ex)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LayoutFixer");
            Directory.CreateDirectory(directory);

            string errorPath = Path.Combine(directory, "error.log");
            File.AppendAllText(
                errorPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Failed to clear diagnostic.log\r\n{ex}\r\n\r\n");
        }
        catch
        {
            // Never let secondary logging hide the original error shown to the user.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Icon?.Dispose();

        base.Dispose(disposing);
    }
}
