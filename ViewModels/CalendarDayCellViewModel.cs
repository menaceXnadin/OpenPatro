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
    private static readonly Color HolidayColor = Color.FromArgb(255, 255, 96, 96);
    private static readonly Color SelectedFillColor = Color.FromArgb(255, 68, 58, 44);
    private static readonly Color SelectedBorderColor = Color.FromArgb(255, 116, 90, 58);
    private static readonly Color TodayBorderColor = Color.FromArgb(255, 138, 145, 156);
    private bool _isSelected;

    public required CalendarDayRecord Record { get; init; }

    public required bool IsCurrentMonth { get; init; }

    public required bool IsToday { get; init; }

    public required bool IsSaturdayColumn { get; init; }

    public string EventText => Record.EventSummary == "--" ? string.Empty : Record.EventSummary;

    public string AdDayText => DateOnly.Parse(Record.AdDateIso).Day.ToString();

    public double CellOpacity => IsCurrentMonth ? 1.0 : 0.32;

    public SolidColorBrush BackgroundBrush => IsSelected
        ? new(SelectedFillColor)
        : Record.IsHoliday
            ? new(Color.FromArgb(34, HolidayColor.R, HolidayColor.G, HolidayColor.B))
            : IsToday
                ? new(Color.FromArgb(20, 255, 255, 255))
                : new(Colors.Transparent);

    public SolidColorBrush BorderBrush => IsSelected
        ? new(SelectedBorderColor)
        : Record.IsHoliday
            ? new(Color.FromArgb(68, HolidayColor.R, HolidayColor.G, HolidayColor.B))
            : IsToday
                ? new(TodayBorderColor)
                : new(Colors.Transparent);

    public SolidColorBrush DayForegroundBrush => !IsCurrentMonth
        ? new(Color.FromArgb(92, 214, 214, 214))
        : IsSelected
            ? new(Color.FromArgb(255, 255, 248, 244))
            : Record.IsHoliday || IsSaturdayColumn
                ? new(HolidayColor)
                : new(Color.FromArgb(255, 244, 244, 244));

    public SolidColorBrush SecondaryForegroundBrush => IsSelected
        ? new(Color.FromArgb(224, 255, 241, 232))
        : Record.IsHoliday || IsSaturdayColumn
            ? new(Color.FromArgb(214, HolidayColor.R, HolidayColor.G, HolidayColor.B))
            : new(Color.FromArgb(188, 226, 226, 226));

    public SolidColorBrush TertiaryForegroundBrush => !IsCurrentMonth
        ? new(Color.FromArgb(78, 194, 194, 194))
        : IsSelected
            ? new(Color.FromArgb(190, 255, 236, 226))
            : Record.IsHoliday || IsSaturdayColumn
                ? new(Color.FromArgb(176, HolidayColor.R, HolidayColor.G, HolidayColor.B))
                : new(Color.FromArgb(156, 192, 192, 192));

    public Thickness BorderThickness => IsSelected || IsToday || Record.IsHoliday ? new Thickness(1.2) : new Thickness(0);

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

    public CornerRadius CornerRadius => new(14);
}