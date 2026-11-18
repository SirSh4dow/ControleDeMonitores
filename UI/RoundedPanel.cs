using System.Drawing.Drawing2D;

namespace MonitorControl.UI;

internal class RoundedPanel : Panel
{
    public int CornerRadius { get; init; } = 14;
    public Color BorderColor { get; set; } = Theme.CardBorder;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 2 || Height <= 2) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scaledRadius = Theme.Scale(this, CornerRadius);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.CreateRoundedRect(rect, scaledRadius);
        using var pen = new Pen(BorderColor, 1F);
        e.Graphics.DrawPath(pen, path);
    }
}
