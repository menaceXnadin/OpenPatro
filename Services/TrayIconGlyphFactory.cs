using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using Microsoft.UI.Xaml;

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
    public static Icon CreateIcon(string text)
    {
        const int size = 64;
        var accentColor = GetAccentColor();

        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var fontFamily = GetFontFamily();
        using var font = new Font(fontFamily, 36f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(accentColor);

        var textSize = graphics.MeasureString(text, font);
        var x = (size - textSize.Width) / 2f;
        var y = (size - textSize.Height) / 2f;

        graphics.DrawString(text, font, brush, x, y);

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

    private static Color GetAccentColor()
    {
        try
        {
            var winColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            return Color.FromArgb(winColor.A, winColor.R, winColor.G, winColor.B);
        }
        catch
        {
            return Color.White;
        }
    }
}