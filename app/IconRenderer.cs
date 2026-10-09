using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BetterMic;

enum IconState { Active, Bypassed, Muted, Problem }

// Draws the tray icon (a microphone) at runtime, so no .ico asset is needed.
static class IconRenderer
{
    public static Icon Render(IconState state, int size)
    {
        var color = state switch
        {
            IconState.Active => Color.FromArgb(80, 200, 120),
            IconState.Bypassed => Color.FromArgb(160, 160, 160),
            IconState.Muted => Color.FromArgb(235, 70, 60),
            _ => Color.FromArgb(240, 170, 40),
        };
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size / 16f;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            // Capsule
            using var capsule = RoundedRect(new RectangleF(5.5f * s, 1f * s, 5f * s, 8.5f * s), 2.5f * s);
            g.FillPath(brush, capsule);
            // Cradle, stem and base
            g.DrawArc(pen, 3f * s, 4.5f * s, 10f * s, 7.5f * s, 0, 180);
            g.DrawLine(pen, 8f * s, 12f * s, 8f * s, 14.2f * s);
            g.DrawLine(pen, 5.5f * s, 14.5f * s, 10.5f * s, 14.5f * s);
            if (state == IconState.Muted)
            {
                // Dark outline under the slash keeps it visible against the red mic
                using var outline = new Pen(Color.FromArgb(40, 0, 0), 3.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var slash = new Pen(Color.White, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(outline, 2.5f * s, 2f * s, 13.5f * s, 14f * s);
                g.DrawLine(slash, 2.5f * s, 2f * s, 13.5f * s, 14f * s);
            }
        }
        // Icon.FromHandle doesn't own the HICON; clone it so the handle can be freed here
        IntPtr hIcon = bmp.GetHicon();
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

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);
}
