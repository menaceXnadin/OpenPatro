using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace OpenPatro.Services;

/// <summary>
/// Creates tray icons with Nepali date text rendered using System.Drawing,
/// which bypasses H.NotifyIcon's GeneratedIconSource font resolution issues.
/// </summary>
public static class TrayIconGlyphFactory
{
    private static PrivateFontCollection? _fontCollection;

    /// <summary>
    /// Creates a System.Drawing.Icon with the given text rendered in the Nepali font.
    /// This is used directly via TaskbarIcon.Icon instead of TaskbarIcon.IconSource,
    /// because GeneratedIconSource internally uses System.Drawing.FontFamily which
    /// cannot load fonts from file paths – only installed system fonts.
    /// </summary>
    public static Icon CreateIcon(string text, bool isHoliday)
    {
        const int size = 64;
        var primaryColor = isHoliday
            ? Color.FromArgb(255, 255, 98, 98)
            : Color.FromArgb(255, 248, 250, 252);
        var shadowColor = isHoliday
            ? Color.FromArgb(180, 40, 0, 0)
            : Color.FromArgb(150, 10, 16, 26);

        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var fontFamily = GetFontFamily();
        using var font = new Font(fontFamily, GetFontSize(text), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(primaryColor);
        using var shadowBrush = new SolidBrush(shadowColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var textBounds = new RectangleF(0, -2, size, size - 2);
        foreach (var offset in GetShadowOffsets())
        {
            var shadowBounds = textBounds;
            shadowBounds.Offset(offset.X, offset.Y);
            graphics.DrawString(text, font, shadowBrush, shadowBounds, format);
        }

        graphics.DrawString(text, font, brush, textBounds, format);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static FontFamily GetFontFamily()
    {
        // Try to load the Nepali font from the app's assets.
        try
        {
            if (_fontCollection is null)
            {
                var baseDir = AppContext.BaseDirectory;
                var fontPath = Path.Combine(baseDir, "Assets", "Fonts", "NotoSansDevanagari.ttf");

                if (File.Exists(fontPath))
                {
                    _fontCollection = new PrivateFontCollection();
                    _fontCollection.AddFontFile(fontPath);
                }
            }

            if (_fontCollection?.Families.Length > 0)
            {
                return _fontCollection.Families[0];
            }
        }
        catch
        {
            // Fall through to fallback.
        }

        // Fallback: use a system font.
        return new FontFamily("Segoe UI");
    }

    private static float GetFontSize(string text)
    {
        return text.Length switch
        {
            <= 1 => 48f,
            2 => 42f,
            _ => 36f
        };
    }

    private static PointF[] GetShadowOffsets()
    {
        return
        [
            new PointF(-1.4f, 0f),
            new PointF(1.4f, 0f),
            new PointF(0f, -1.4f),
            new PointF(0f, 1.4f),
            new PointF(-1f, -1f),
            new PointF(1f, -1f),
            new PointF(-1f, 1f),
            new PointF(1f, 1f)
        ];
    }
}