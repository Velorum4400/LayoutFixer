using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class SelectableLabel : TextBox
{
    [DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    public SelectableLabel()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        BackColor = System.Drawing.SystemColors.Control;
        Cursor = Cursors.Arrow;
        ShortcutsEnabled = true;
        TabStop = false;
        Multiline = true;
        WordWrap = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        HideCaret(Handle);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        BeginInvoke(new Action(() => HideCaret(Handle)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        BeginInvoke(new Action(() => HideCaret(Handle)));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Keep Ctrl+C / Ctrl+A working, but do not expose the caret.
        base.OnKeyDown(e);
        BeginInvoke(new Action(() => HideCaret(Handle)));
    }
}
