using System;
using Microsoft.UI.Windowing;

namespace OpenPatro.Services;

/// <summary>
/// Centralizes all window-sizing policy so that layout constants, DPI conversions,
/// work-area calculations, and calendar-grid sizing live in one authoritative place
/// instead of being scattered across the MainWindow code-behind.
/// </summary>
public sealed class WindowLayoutService
{
    // ── Window sizing constants (previously magic numbers in MainWindow.xaml.cs) ──

    /// <summary>Preferred window width in DIPs when the work area is large enough.</summary>
    public const double PreferredWindowWidthDip = 1160;

    /// <summary>Preferred window height in DIPs when the work area is large enough.</summary>
    public const double PreferredWindowHeightDip = 1050;

    /// <summary>Hard-minimum width in DIPs; the window can never be smaller than this.</summary>
    public const double AbsoluteMinWidthDip = 800;

    /// <summary>Hard-minimum height in DIPs; the window can never be smaller than this.</summary>
    public const double AbsoluteMinHeightDip = 600;

    /// <summary>Fraction of the work-area width used when the preferred size is too big.</summary>
    public const double WorkAreaWidthRatio = 0.72;

    /// <summary>Fraction of the work-area height used when the preferred size is too big.</summary>
    public const double WorkAreaHeightRatio = 0.88;

    /// <summary>Pixel padding subtracted from the work area to avoid edge-to-edge windows.</summary>
    public const int WorkAreaPaddingPx = 24;

    // ── Calendar grid constants (previously anonymous in UpdateCalendarLayout) ──

    /// <summary>Number of columns (days in a week).</summary>
    public const int CalendarColumns = 7;

    /// <summary>Number of rows in the calendar grid.</summary>
    public const int CalendarRows = 6;

    /// <summary>Minimum calendar cell width in DIPs.</summary>
    public const double MinCellSizeDip = 72;

    /// <summary>Maximum calendar cell width in DIPs.</summary>
    public const double MaxCellSizeDip = 260;

    /// <summary>Cell height = cell width × this ratio.</summary>
    public const double CellAspectRatio = 0.92;

    /// <summary>
    /// Small buffer added to the computed grid width so the internal ScrollViewer
    /// border overhead doesn't force the ItemsWrapGrid to wrap at 6 columns.
    /// </summary>
    public const double GridWidthBufferPx = 4;

    /// <summary>
    /// Margin below the weekday header border, subtracted from available height.
    /// </summary>
    public const double WeekdayHeaderBottomMarginDip = 10;

    // ── DPI helpers ──

    /// <summary>Standard DPI (100 % scaling).</summary>
    private const uint BaseDpi = 96;

    /// <summary>Converts a DIP value to device pixels at the given DPI.</summary>
    public static int DipToPx(double dip, uint dpi)
    {
        return (int)Math.Ceiling(dip * dpi / BaseDpi);
    }

    /// <summary>Returns the supplied <paramref name="rawDpi"/> or 96 if it is zero.</summary>
    public static uint SafeDpi(uint rawDpi)
    {
        return rawDpi == 0 ? BaseDpi : rawDpi;
    }

    // ── Window sizing policy ──

    /// <summary>
    /// Computes the minimum allowed window size in physical pixels for the WM_GETMINMAXINFO hook.
    /// </summary>
    public static (int WidthPx, int HeightPx) GetMinimumSizePx(uint dpi)
    {
        dpi = SafeDpi(dpi);
        return (DipToPx(AbsoluteMinWidthDip, dpi), DipToPx(AbsoluteMinHeightDip, dpi));
    }

    /// <summary>
    /// Computes the target window size (in physical pixels) based on display work area
    /// and the preferred DIP size. Falls back to DPI-scaled preferred size when the
    /// work area can't be read.
    /// </summary>
    public static (int WidthPx, int HeightPx) GetTargetSizePx(uint dpi, AppWindow appWindow)
    {
        dpi = SafeDpi(dpi);

        var preferredWidthPx = DipToPx(PreferredWindowWidthDip, dpi);
        var preferredHeightPx = DipToPx(PreferredWindowHeightDip, dpi);

        var (absoluteMinWidthPx, absoluteMinHeightPx) = GetMinimumSizePx(dpi);

        var maxWidthPx = int.MaxValue;
        var maxHeightPx = int.MaxValue;
        var targetWidthPx = preferredWidthPx;
        var targetHeightPx = preferredHeightPx;

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            maxWidthPx = Math.Max(480, workArea.Width - WorkAreaPaddingPx);
            maxHeightPx = Math.Max(480, workArea.Height - WorkAreaPaddingPx);

            targetWidthPx = (int)Math.Floor(maxWidthPx * WorkAreaWidthRatio);
            targetHeightPx = (int)Math.Floor(maxHeightPx * WorkAreaHeightRatio);
        }
        catch
        {
            // Fall back to DPI-scaled preferred sizing.
        }

        var effectiveMinWidthPx = Math.Min(absoluteMinWidthPx, maxWidthPx);
        var effectiveMinHeightPx = Math.Min(absoluteMinHeightPx, maxHeightPx);

        var widthPx = Math.Clamp(targetWidthPx, effectiveMinWidthPx, maxWidthPx);
        var heightPx = Math.Clamp(targetHeightPx, effectiveMinHeightPx, maxHeightPx);

        return (widthPx, heightPx);
    }

    // ── Calendar grid layout policy ──

    /// <summary>
    /// Given the available width and height (DIPs) of the calendar layout host,
    /// computes the cell size and total grid width for the 7×6 calendar grid.
    /// Returns null if the available space is too small for computation.
    /// </summary>
    public static CalendarGridMetrics? ComputeCalendarGridMetrics(
        double availableWidth, double availableHeight, double weekdayHeaderHeight)
    {
        if (availableWidth <= 0)
        {
            return null;
        }

        // Subtract the header height + bottom margin from the vertical budget.
        var usableHeight = Math.Max(0, availableHeight - weekdayHeaderHeight - WeekdayHeaderBottomMarginDip);

        var widthFromAvailableWidth = Math.Floor(availableWidth / CalendarColumns);
        var widthFromAvailableHeight = Math.Floor(usableHeight / CalendarRows / CellAspectRatio);

        // Use the smaller so the grid fits in both dimensions.
        var itemWidth = Math.Min(widthFromAvailableWidth, widthFromAvailableHeight);
        itemWidth = Math.Clamp(itemWidth, MinCellSizeDip, MaxCellSizeDip);

        var itemHeight = Math.Floor(itemWidth * CellAspectRatio);
        itemHeight = Math.Clamp(itemHeight, MinCellSizeDip * CellAspectRatio, MaxCellSizeDip * CellAspectRatio);

        var gridWidth = (itemWidth * CalendarColumns) + GridWidthBufferPx;

        return new CalendarGridMetrics(itemWidth, itemHeight, gridWidth);
    }
}

/// <summary>
/// Computed calendar grid dimensions.
/// </summary>
public sealed record CalendarGridMetrics(double ItemWidth, double ItemHeight, double GridWidth);
