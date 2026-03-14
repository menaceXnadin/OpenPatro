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
    private bool _isSelected;

    public required CalendarDayRecord Record { get; init; }

    public required bool IsCurrentMonth { get; init; }

    public required bool IsToday { get; init; }

    public required bool IsSaturdayColumn { get; init; }

    public string EventText => Record.EventSummary == "--" ? string.Empty : Record.EventSummary;

    public string AdDayText => DateOnly.Parse(Record.AdDateIso).Day.ToString();

    public double CellOpacity => IsCurrentMonth ? 1.0 : 0.32;

    public SolidColorBrush BackgroundBrush => Record.IsHoliday
        ? new(Color.FromArgb(24, 255, 170, 102))
        : new(Colors.Transparent);

    public SolidColorBrush BorderBrush => IsToday
        ? new((Color)Application.Current.Resources["SystemAccentColor"])
        : new(Color.FromArgb(18, 255, 255, 255));

    public SolidColorBrush DayForegroundBrush => IsSaturdayColumn
        ? new(Color.FromArgb(210, 235, 235, 235))
        : new(Color.FromArgb(255, 245, 245, 245));

    public SolidColorBrush SecondaryForegroundBrush => new(Color.FromArgb(190, 235, 235, 235));

    public SolidColorBrush TertiaryForegroundBrush => new(Color.FromArgb(170, 235, 235, 235));

    public Thickness BorderThickness => IsToday ? new Thickness(1.5) : new Thickness(1);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public CornerRadius CornerRadius => new(8);
}