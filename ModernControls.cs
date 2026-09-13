using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LayoutFixer;

public sealed class GradientHeaderPanel : Panel
{
    public Color Color1 { get; set; } = Color.FromArgb(10, 70, 155);
    public Color Color2 { get; set; } = Color.FromArgb(6, 32, 85);

    public GradientHeaderPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
            return;

        using var brush = new LinearGradientBrush(
            ClientRectangle,
            Color1,
            Color2,
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

public sealed class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 18;
    public Color BorderColor { get; set; } = Color.FromArgb(220, 226, 236);

    public CardPanel()
    {
        BackColor = Color.White;
        Padding = new Padding(24);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnResize(System.EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
        Parent?.Invalidate(Bounds, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        using GraphicsPath path = RoundedRect(rect, CornerRadius);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}

public sealed class ModernButton : Button
{
    private bool _primary;

    public bool Primary
    {
        get => _primary;
        set
        {
            _primary = value;
            ApplyStyle();
        }
    }

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        Height = 42;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        Cursor = Cursors.Hand;
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        if (_primary)
        {
            BackColor = Color.FromArgb(20, 112, 235);
            ForeColor = Color.White;
            FlatAppearance.BorderColor = Color.FromArgb(20, 112, 235);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 96, 210);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(12, 82, 186);
        }
        else
        {
            BackColor = Color.White;
            ForeColor = Color.FromArgb(25, 42, 70);
            FlatAppearance.BorderColor = Color.FromArgb(198, 207, 221);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 249, 253);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(235, 241, 249);
        }
    }
}
