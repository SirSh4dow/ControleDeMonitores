using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MonitorControl.UI;

internal static class Theme
{
    // Backgrounds & Borders
    public static readonly Color AppBack = Color.FromArgb(245, 247, 251);
    public static readonly Color SurfaceWhite = Color.White;
    public static readonly Color InputBack = Color.FromArgb(248, 249, 252);
    public static readonly Color CardBorder = Color.FromArgb(228, 232, 240);
    public static readonly Color DividerLine = Color.FromArgb(234, 237, 243);
    public static readonly Color ButtonBorder = Color.FromArgb(214, 220, 230);

    // Brand & Accent Colors
    public static readonly Color AccentBlue = Color.FromArgb(67, 97, 238);
    public static readonly Color AccentBlueHover = Color.FromArgb(54, 81, 212);
    public static readonly Color AccentBlueDisabled = Color.FromArgb(196, 206, 246);
    public static readonly Color InactiveAccent = Color.FromArgb(203, 213, 225);

    // Status Badges
    public static readonly Color ActiveBadgeBack = Color.FromArgb(230, 247, 238);
    public static readonly Color ActiveBadgeFore = Color.FromArgb(21, 128, 61);
    public static readonly Color PrimaryBadgeBack = Color.FromArgb(235, 240, 255);
    public static readonly Color PrimaryBadgeFore = Color.FromArgb(52, 78, 210);
    public static readonly Color InactiveBadgeBack = Color.FromArgb(241, 245, 249);
    public static readonly Color InactiveBadgeFore = Color.FromArgb(100, 116, 139);

    // Typography Colors
    public static readonly Color TextPrimary = Color.FromArgb(24, 30, 44);
    public static readonly Color TextSecondary = Color.FromArgb(100, 110, 128);
    public static readonly Color TextMuted = Color.FromArgb(122, 131, 148);
    public static readonly Color ButtonFore = Color.FromArgb(55, 65, 81);

    // Shared static fonts (never disposed during app lifetime; eliminates per-refresh HFONT leaks)
    public static readonly Font AppDefaultFont = new("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font HeaderMarkFont = new("Segoe UI Symbol", 22F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font HeaderTitleFont = new("Segoe UI Semibold", 20F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font HeaderSubtitleFont = new("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font CardTitleFont = new("Segoe UI Semibold", 12.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font CardSubtitleFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FieldLabelFont = new("Segoe UI Semibold", 7.8F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BadgeFont = new("Segoe UI Semibold", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font ButtonFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font ButtonSemiboldFont = new("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font ComboFont = new("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font OverlayNumberFont = new("Segoe UI Semibold", 56F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font OverlayTitleFont = new("Segoe UI Semibold", 13F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font DialogTitleFont = new("Segoe UI Semibold", 12F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font CanvasNumberFont = new("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font CanvasSmallFont = new("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);

    public static int Scale(Control control, int basePixels) =>
        (int)Math.Round(basePixels * (control.DeviceDpi / 96.0));

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Monitor bezel
            using var bgBrush = new SolidBrush(AccentBlue);
            using var bezelPath = CreateRoundedRect(new Rectangle(6, 8, 52, 36), 8);
            g.FillPath(bgBrush, bezelPath);

            // Inner screen highlight
            using var screenBrush = new SolidBrush(Color.White);
            using var innerPath = CreateRoundedRect(new Rectangle(12, 14, 40, 24), 4);
            g.FillPath(screenBrush, innerPath);

            // Monitor stand neck & base
            using var standPen = new Pen(AccentBlue, 5F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(standPen, 32, 45, 32, 54);
            g.DrawLine(standPen, 20, 55, 44, 55);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        diameter = Math.Min(diameter, Math.Min(rect.Width, rect.Height));

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
