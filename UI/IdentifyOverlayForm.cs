using System.Drawing.Drawing2D;
using MonitorControl.Models;
using MonitorControl.Services;

namespace MonitorControl.UI;

internal sealed class IdentifyOverlayForm : Form
{
    private readonly int _number;
    private readonly string _caption;

    private IdentifyOverlayForm(Rectangle screenBounds, int number, string caption)
    {
        _number = number;
        _caption = caption;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 30, 44);
        ForeColor = Color.White;
        Opacity = 0.92;
        DoubleBuffered = true;

        var width = Math.Min(420, Math.Max(260, screenBounds.Width / 4));
        var height = Math.Min(230, Math.Max(165, screenBounds.Height / 4));
        Bounds = new Rectangle(
            screenBounds.Left + (screenBounds.Width - width) / 2,
            screenBounds.Top + (screenBounds.Height - height) / 2,
            width,
            height);

        Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width > 0 && Height > 0)
        {
            using var path = Theme.CreateRoundedRect(new Rectangle(0, 0, Width, Height), Theme.Scale(this, 20));
            var previousRegion = Region;
            Region = new Region(path);
            previousRegion?.Dispose();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var numberText = _number.ToString();
        var numberRect = new Rectangle(0, Theme.Scale(this, 12), Width, (int)(Height * 0.62));
        TextRenderer.DrawText(
            e.Graphics,
            numberText,
            Theme.OverlayNumberFont,
            numberRect,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var captionRect = new Rectangle(
            Theme.Scale(this, 16),
            (int)(Height * 0.66),
            Width - Theme.Scale(this, 32),
            (int)(Height * 0.28));
        TextRenderer.DrawText(
            e.Graphics,
            _caption,
            Theme.OverlayTitleFont,
            captionRect,
            Color.FromArgb(203, 213, 225),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var borderPen = new Pen(Theme.AccentBlue, Theme.Scale(this, 2));
        using var borderPath = Theme.CreateRoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), Theme.Scale(this, 20));
        e.Graphics.DrawPath(borderPen, borderPath);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var currentRegion = Region;
            Region = null;
            currentRegion?.Dispose();
        }
        base.Dispose(disposing);
    }

    public static List<IdentifyOverlayForm> CreateAndShowForDevices(IEnumerable<DisplayDevice> devices)
    {
        var screens = Screen.AllScreens;
        var overlays = new List<IdentifyOverlayForm>();

        foreach (var device in devices.Where(d => d.IsActive))
        {
            var matchedScreen = screens.FirstOrDefault(s => DisplayService.SameGdi(s.DeviceName, device.GdiName));
            Rectangle bounds;
            if (matchedScreen is not null)
            {
                bounds = matchedScreen.Bounds;
            }
            else if (device.CurrentMode is not null)
            {
                bounds = new Rectangle(
                    device.Position.X,
                    device.Position.Y,
                    (int)device.CurrentMode.Width,
                    (int)device.CurrentMode.Height);
            }
            else
            {
                continue;
            }

            var role = device.IsPrimary ? " • Principal" : string.Empty;
            var caption = $"{device.DisplayTitle} ({device.ConnectorName}{role})";
            var overlay = new IdentifyOverlayForm(bounds, device.DisplayNumber, caption);
            overlay.Show();
            overlays.Add(overlay);
        }

        return overlays;
    }
}
