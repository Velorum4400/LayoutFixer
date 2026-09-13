using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class HotkeyCaptureBox : TextBox
{
    private readonly HashSet<Keys> _held = new();
    private readonly List<Keys> _captured = new();
    private bool _capturing;

    public string Hotkey
    {
        get => Text;
        set => Text = HotkeyDefinition.IsValid(value)
            ? HotkeyDefinition.Format(HotkeyDefinition.Parse(value))
            : "";
    }

    public HotkeyCaptureBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        BackColor = SystemColors.Window;
        TabStop = true;

        PreviewKeyDown += (_, e) => e.IsInputKey = true;
        MouseDown += (_, _) => Focus();
        Enter += (_, _) => SelectAll();
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        Keys key = HotkeyDefinition.Normalize(e.KeyCode);

        if (!_capturing)
        {
            _capturing = true;
            _held.Clear();
            _captured.Clear();
        }

        _held.Add(key);

        if (!_captured.Contains(key))
        {
            if (_captured.Count >= HotkeyDefinition.MaxKeys)
            {
                SystemSounds.Beep.Play();
                return;
            }

            _captured.Add(key);
        }

        Text = HotkeyDefinition.Format(_captured);
        SelectionStart = TextLength;
        SelectionLength = 0;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        Keys key = HotkeyDefinition.Normalize(e.KeyCode);
        _held.Remove(key);

        if (_capturing && _held.Count == 0)
        {
            Text = HotkeyDefinition.Format(_captured);
            _capturing = false;
            SelectAll();
        }
    }

    protected override void OnLostFocus(System.EventArgs e)
    {
        if (_capturing)
        {
            Text = HotkeyDefinition.Format(_captured);
            _held.Clear();
            _capturing = false;
        }

        base.OnLostFocus(e);
    }
}
