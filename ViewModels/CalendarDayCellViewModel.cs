using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using OpenPatro.Infrastructure;
using OpenPatro.Models;

namespace OpenPatro.ViewModels;

public sealed class CalendarDayCellViewModel : BindableBase
{
    // ── Refined colour palette ──────────────────────────────────────────
    // Holiday accent – a warm coral instead of raw red
    private static readonly Color HolidayColor = Color.FromArgb(255, 239, 108, 108);

    // Selected state – a warm amber tint
    private static readonly Color SelectedFillColor = Color.FromArgb(255, 58, 52, 46);
    private static readonly Color SelectedBorderColor = Color.FromArgb(255, 168, 132, 88);

    // Today – subtle white ring
    private static readonly Color TodayBorderColor = Color.FromArgb(255, 128, 138, 158);
    private static readonly Color TodayFillColor = Color.FromArgb(18, 255, 255, 255);

    private bool _isSelected;

    public required CalendarDayRecord Record { get; init; }

    public required bool IsCurrentMonth { get; init; }

    public required bool IsToday { get; init; }

    public required bool IsSaturdayColumn { get; init; }

    public string EventText => Record.EventSummary == "--" ? string.Empty : Record.EventSummary;

    public string AdDayText => DateOnly.Parse(Record.AdDateIso).Day.ToString();

    public double CellOpacity => IsCurrentMonth ? 1.0 : 0.28;

    // ── Background ──────────────────────────────────────────────────────
    public SolidColorBrush BackgroundBrush => IsSelected
        ? new(SelectedFillColor)
        : Record.IsHoliday
            ? new(Color.FromArgb(22, HolidayColor.R, HolidayColor.G, HolidayColor.B))
            : IsToday
                ? new(TodayFillColor)
                : new(Colors.Transparent);

    // ── Border ──────────────────────────────────────────────────────────
    public SolidColorBrush BorderBrush => IsSelected
        ? new(SelectedBorderColor)
        : Record.IsHoliday
            ? new(Color.FromArgb(48, HolidayColor.R, HolidayColor.G, HolidayColor.B))
            : IsToday
                ? new(TodayBorderColor)
                : new(Colors.Transparent);

    // ── Day number foreground ───────────────────────────────────────────
    public SolidColorBrush DayForegroundBrush => !IsCurrentMonth
        ? new(Color.FromArgb(72, 200, 200, 200))
        : IsSelected
            ? new(Color.FromArgb(255, 255, 250, 244))
            : Record.IsHoliday || IsSaturdayColumn
                ? new(HolidayColor)
                : new(Color.FromArgb(255, 236, 238, 242));

    // ── Event text foreground ───────────────────────────────────────────
    public SolidColorBrush SecondaryForegroundBrush => IsSelected
        ? new(Color.FromArgb(210, 255, 241, 228))
        : Record.IsHoliday || IsSaturdayColumn
            ? new(Color.FromArgb(180, HolidayColor.R, HolidayColor.G, HolidayColor.B))
            : new(Color.FromArgb(158, 210, 214, 222));

    // ── Tithi / AD-day foreground ───────────────────────────────────────
    public SolidColorBrush TertiaryForegroundBrush => !IsCurrentMonth
        ? new(Color.FromArgb(58, 180, 180, 180))
        : IsSelected
            ? new(Color.FromArgb(170, 255, 236, 220))
            : Record.IsHoliday || IsSaturdayColumn
                ? new(Color.FromArgb(140, HolidayColor.R, HolidayColor.G, HolidayColor.B))
                : new(Color.FromArgb(120, 180, 184, 196));

    // ── Geometry ────────────────────────────────────────────────────────
    public Thickness BorderThickness => IsSelected || IsToday || Record.IsHoliday
        ? new Thickness(1)
        : new Thickness(0);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                RaisePropertyChanged(nameof(BackgroundBrush));
                RaisePropertyChanged(nameof(BorderBrush));
                RaisePropertyChanged(nameof(DayForegroundBrush));
                RaisePropertyChanged(nameof(SecondaryForegroundBrush));
                RaisePropertyChanged(nameof(TertiaryForegroundBrush));
                RaisePropertyChanged(nameof(BorderThickness));
            }
        }
    }

    public CornerRadius CornerRadius => new(12);
}