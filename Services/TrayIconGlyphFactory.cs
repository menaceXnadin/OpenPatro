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

    public static Icon CreateIcon(string text, bool isHoliday)
    {
        return CreateIcon(text, null, isHoliday);
    }

    /// <summary>
    /// Creates a System.Drawing.Icon with the given text rendered in the Nepali font.
    /// This is used directly via TaskbarIcon.Icon instead of TaskbarIcon.IconSource,
    /// because GeneratedIconSource internally uses System.Drawing.FontFamily which
    /// cannot load fonts from file paths – only installed system fonts.
    /// </summary>
    public static Icon CreateIcon(string dayText, string? monthText, bool isHoliday)
    {
        // Use a high-resolution canvas; Windows will downscale to the actual
        // tray-icon size but preserves detail, so text stays sharp and legible.
        const int size = 256;
        var primaryColor = isHoliday
            ? Color.FromArgb(255, 255, 98, 98)
            : Color.FromArgb(255, 248, 250, 252);
        var shadowColor = isHoliday
            ? Color.FromArgb(140, 40, 0, 0)
            : Color.FromArgb(120, 10, 16, 26);

        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var fontFamily = GetFontFamily();
        using var brush = new SolidBrush(primaryColor);
        using var shadowBrush = new SolidBrush(shadowColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        if (string.IsNullOrWhiteSpace(monthText))
        {
            // Keep day-only digits narrower so 2-digit dates stay readable after tray downscaling.
            using var font = new Font(fontFamily, FitFontSize(graphics, dayText, fontFamily, 180f, size * 0.72f), FontStyle.Bold, GraphicsUnit.Pixel);
            var textBounds = new RectangleF(0, 0, size, size);
            foreach (var offset in GetShadowOffsets())
            {
                var shadowBounds = textBounds;
                shadowBounds.Offset(offset.X, offset.Y);
                graphics.DrawString(dayText, font, shadowBrush, shadowBounds, format);
            }

            graphics.DrawString(dayText, font, brush, textBounds, format);
            return Icon.FromHandle(bitmap.GetHicon());
        }

        // Day gets the top ~55%, month gets the bottom ~45%.
        // Font sizes are scaled proportionally to the 256px canvas.
        using var dayFont = new Font(fontFamily, FitFontSize(graphics, dayText, fontFamily, 140f, size - 16), FontStyle.Bold, GraphicsUnit.Pixel);
        using var monthFont = new Font(fontFamily, GetMonthFontSize(monthText), FontStyle.Bold, GraphicsUnit.Pixel);

        var dayBounds = new RectangleF(0, -4, size, 152);
        var monthBounds = new RectangleF(0, 140, size, 112);

        foreach (var offset in GetShadowOffsets())
        {
            var dayShadowBounds = dayBounds;
            dayShadowBounds.Offset(offset.X, offset.Y);
            graphics.DrawString(dayText, dayFont, shadowBrush, dayShadowBounds, format);

            var monthShadowBounds = monthBounds;
            monthShadowBounds.Offset(offset.X, offset.Y);
            graphics.DrawString(monthText, monthFont, shadowBrush, monthShadowBounds, format);
        }

        graphics.DrawString(dayText, dayFont, brush, dayBounds, format);
        graphics.DrawString(monthText, monthFont, brush, monthBounds, format);

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

    /// <summary>
    /// Starts at <paramref name="startSize"/> and shrinks the font by 2px at a time
    /// until the rendered text fits within <paramref name="maxWidth"/>.
    /// Uses MeasureCharacterRanges for true ink-bounds (no GDI+ internal padding),
    /// which maximises the usable font size for Devanagari glyphs.
    /// </summary>
    private static float FitFontSize(
        Graphics graphics, string text, FontFamily family, float startSize, float maxWidth)
    {
        using var measuringFormat = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var ranges = new[] { new CharacterRange(0, text.Length) };
        measuringFormat.SetMeasurableCharacterRanges(ranges);

        var containerRect = new RectangleF(0, 0, 4096, 4096);
        var size = startSize;
        while (size > 10f)
        {
            using var probe = new Font(family, size, FontStyle.Bold, GraphicsUnit.Pixel);
            var regions = graphics.MeasureCharacterRanges(text, probe, containerRect, measuringFormat);
            var inkBounds = regions[0].GetBounds(graphics);
            if (inkBounds.Width <= maxWidth)
            {
                return size;
            }

            size -= 2f;
        }

        return size;
    }

    private static float GetMonthFontSize(string text)
    {
        // Month font sizes scaled for the 256px canvas.
        return text.Length switch
        {
            <= 2 => 76f,
            <= 3 => 68f,
            <= 4 => 60f,
            <= 6 => 52f,
            _ => 46f
        };
    }

    private static PointF[] GetShadowOffsets()
    {
        // Shadow offsets scaled to the 256px canvas for a consistent outline.
        return
        [
            new PointF(-3f, 0f),
            new PointF(3f, 0f),
            new PointF(0f, -3f),
            new PointF(0f, 3f),
            new PointF(-2f, -2f),
            new PointF(2f, -2f),
            new PointF(-2f, 2f),
            new PointF(2f, 2f)
        ];
    }
}