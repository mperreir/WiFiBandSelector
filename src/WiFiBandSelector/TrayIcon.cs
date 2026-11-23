using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WiFiBandSelector;

/// <summary>Icône de la zone de notification, dessinée à la volée selon la bande et le verrou.</summary>
internal static class TrayIcon
{
    private static readonly Color AutoColor = Color.FromArgb(84, 110, 122);
    private static readonly Color Band24Color = Color.FromArgb(232, 113, 10);
    private static readonly Color Band5Color = Color.FromArgb(30, 142, 62);
    private static readonly Color Band6Color = Color.FromArgb(123, 31, 162);
    private static readonly Color DisabledColor = Color.FromArgb(158, 158, 158);

    public static Icon Create(Band? band, bool locked, bool unavailable)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = Render(size, band, locked, unavailable);
        var handle = bitmap.GetHicon();
        try
        {
            using var native = Icon.FromHandle(handle);
            return (Icon)native.Clone();
        }
        finally
        {
            Interop.ReleaseIcon(handle);
        }
    }

    private static Bitmap Render(int size, Band? band, bool locked, bool unavailable)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var color = unavailable ? DisabledColor : ColorFor(band);
        var penWidth = Math.Max(1.5f, size * 0.11f);
        var cx = size * 0.46f;
        var cy = size * 0.84f;

        using (var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            foreach (var radiusFactor in new[] { 0.17f, 0.30f, 0.43f })
            {
                var radius = size * radiusFactor;
                g.DrawArc(pen, cx - radius, cy - radius, radius * 2, radius * 2, 225, 90);
            }
        }

        using (var dot = new SolidBrush(color))
        {
            var dotRadius = penWidth * 0.62f;
            g.FillEllipse(dot, cx - dotRadius, cy - dotRadius, dotRadius * 2, dotRadius * 2);
        }

        if (unavailable)
            return bitmap;

        var badgeRadius = size * (size >= 24 ? 0.27f : 0.24f);
        var badgeX = size - badgeRadius - size * 0.02f;
        var badgeY = size - badgeRadius - size * 0.02f;

        using (var badge = new SolidBrush(color))
            g.FillEllipse(badge, badgeX - badgeRadius, badgeY - badgeRadius, badgeRadius * 2, badgeRadius * 2);

        if (locked)
        {
            using var ring = new Pen(Color.White, Math.Max(1f, size * 0.055f));
            g.DrawEllipse(
                ring,
                badgeX - badgeRadius + ring.Width / 2,
                badgeY - badgeRadius + ring.Width / 2,
                (badgeRadius - ring.Width / 2) * 2,
                (badgeRadius - ring.Width / 2) * 2);
        }

        var glyph = band switch
        {
            Band.Band24 => "2",
            Band.Band5 => "5",
            Band.Band6 => "6",
            _ => "A",
        };

        if (size < 24)
            return bitmap;

        using var font = new Font("Segoe UI", badgeRadius * 1.15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var text = new SolidBrush(Color.White);
        g.DrawString(glyph, font, text, new RectangleF(
            badgeX - badgeRadius, badgeY - badgeRadius, badgeRadius * 2, badgeRadius * 2), format);

        return bitmap;
    }

    private static Color ColorFor(Band? band) => band switch
    {
        Band.Band24 => Band24Color,
        Band.Band5 => Band5Color,
        Band.Band6 => Band6Color,
        _ => AutoColor,
    };
}
