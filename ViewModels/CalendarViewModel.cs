using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using OpenPatro.Infrastructure;
using OpenPatro.Models;
using OpenPatro.Services;

namespace OpenPatro.ViewModels;

public sealed class CalendarViewModel : BindableBase
{
    public sealed class MonthNavigationOption
    {
        public int MonthNumber { get; init; }

        public string DisplayName { get; init; } = string.Empty;
    }

    private readonly AppServices _services;
    private CalendarDayRecord? _todayRecord;
    private CalendarDayCellViewModel? _selectedDay;
    private string _selectedNote = string.Empty;
    private bool _isBusy;
    private string _monthTitleNepali = string.Empty;
    private string _monthTitleEnglish = string.Empty;
    private int _displayYear;
    private int _displayMonth;
    private int _selectedNavigationYear;
    private MonthNavigationOption? _selectedNavigationMonth;

    private static readonly string[] NepaliMonthNames =
    [
        "बैशाख", "जेठ", "असार", "साउन", "भदौ", "असोज",
        "कार्तिक", "मंसिर", "पुष", "माघ", "फागुन", "चैत"
    ];

    public CalendarViewModel(AppServices services)
    {
        _services = services;
        PreviousMonthCommand = new AsyncRelayCommand(LoadPreviousMonthAsync, () => !IsBusy);
        NextMonthCommand = new AsyncRelayCommand(LoadNextMonthAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SelectDayCommand = new AsyncRelayCommand(SelectDayAsync);
        SaveNoteCommand = new AsyncRelayCommand(SaveNoteAsync, () => SelectedDay is not null);
        OpenMainWindowCommand = new AsyncRelayCommand(async () => await ((App)Application.Current).ShowMainWindowAsync());
    }

    public ObservableCollection<CalendarDayCellViewModel> Days { get; } = new();

    public ObservableCollection<int> AvailableYears { get; } = new();

    public ObservableCollection<MonthNavigationOption> AvailableMonths { get; } = new();

    public ICommand PreviousMonthCommand { get; }

    public ICommand NextMonthCommand { get; }

    public ICommand SelectDayCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SaveNoteCommand { get; }

    public ICommand OpenMainWindowCommand { get; }

    public string MonthTitleNepali
    {
        get => _monthTitleNepali;
        private set => SetProperty(ref _monthTitleNepali, value);
    }

    public string MonthTitleEnglish
    {
        get => _monthTitleEnglish;
        private set => SetProperty(ref _monthTitleEnglish, value);
    }

    public string TodayBsFullDate => _todayRecord?.BsFullDate ?? string.Empty;

    public string TodayAdDateText => _todayRecord?.AdDateText ?? string.Empty;

    public CalendarDayCellViewModel? SelectedDay
    {
        get => _selectedDay;
        private set
        {
            if (SetProperty(ref _selectedDay, value))
            {
                RaisePropertyChanged(nameof(HasSelectedDay));
                RaisePropertyChanged(nameof(SelectedDayVisibility));
                RaisePropertyChanged(nameof(SelectedEventText));
                RaisePropertyChanged(nameof(SelectedHolidayLabel));
                RaisePropertyChanged(nameof(SelectedHolidayVisibility));
                RaisePropertyChanged(nameof(SelectedBsFullDate));
                RaisePropertyChanged(nameof(SelectedAdDateText));
                RaisePropertyChanged(nameof(SelectedLunarText));
                RaisePropertyChanged(nameof(SelectedPanchanga));
                ((AsyncRelayCommand)SaveNoteCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public int SelectedNavigationYear
    {
        get => _selectedNavigationYear;
        private set => SetProperty(ref _selectedNavigationYear, value);
    }

    public MonthNavigationOption? SelectedNavigationMonth
    {
        get => _selectedNavigationMonth;
        private set => SetProperty(ref _selectedNavigationMonth, value);
    }

    public async Task NavigateToYearAsync(int year)
    {
        if (year == _displayYear)
        {
            return;
        }

        var months = await _services.CalendarRepository.GetAvailableMonthsForYearAsync(year);
        if (months.Count == 0)
        {
            return;
        }

        var targetMonth = months.Any(m => m.BsMonth == _displayMonth)
            ? _displayMonth
            : months[0].BsMonth;

        ClearSelection();
        await LoadMonthAsync(year, targetMonth);
    }

    public async Task NavigateToMonthAsync(int month)
    {
        if (month == _displayMonth)
        {
            return;
        }

        ClearSelection();
        await LoadMonthAsync(_displayYear, month);
    }

    public bool HasSelectedDay => SelectedDay is not null;

    public Visibility SelectedDayVisibility => HasSelectedDay ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedEventText => SelectedDay?.EventText.Length > 0 ? SelectedDay.EventText : "No listed event";

    public string SelectedHolidayLabel => SelectedDay?.Record.IsHoliday == true ? "Holiday" : string.Empty;

    public Visibility SelectedHolidayVisibility => SelectedDay?.Record.IsHoliday == true ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedBsFullDate => SelectedDay?.Record.BsFullDate ?? string.Empty;

    public string SelectedAdDateText => SelectedDay?.Record.AdDateText ?? string.Empty;

    public string SelectedLunarText => SelectedDay?.Record.LunarText ?? string.Empty;

    public string SelectedPanchanga => SelectedDay?.Record.Panchanga ?? string.Empty;

    public string SelectedNote
    {
        get => _selectedNote;
        set => SetProperty(ref _selectedNote, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)PreviousMonthCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)NextMonthCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync()
    {
        ClearSelection();

        var today = await _services.CalendarRepository.GetTodayAsync();
        SetTodayRecord(today);
        if (today is null)
        {
            var currentYear = 2082;
            var currentMonth = 11;
            await LoadMonthAsync(currentYear, currentMonth);
            return;
        }

        await LoadMonthAsync(today.BsYear, today.BsMonth);
    }

    public async Task EnsureLoadedAsync()
    {
        if (Days.Count == 0)
        {
            await InitializeAsync();
        }
    }

    public async Task RefreshAsync()
    {
        ClearSelection();

        if (_displayYear <= 0 || _displayMonth <= 0)
        {
            await InitializeAsync();
            return;
        }

        await LoadMonthAsync(_displayYear, _displayMonth);
    }

    public async Task LoadMonthAsync(int year, int month)
    {
        IsBusy = true;
        try
        {
            _displayYear = year;
            _displayMonth = month;

            // Try to sync from network, but don't let failures block the UI.
            var previous = CalendarSyncService.GetPreviousMonth(year, month);
            var next = CalendarSyncService.GetNextMonth(year, month);
            try
            {
                await _services.CalendarSync.EnsureMonthPresentAsync(year, month);
                await _services.CalendarSync.EnsureMonthPresentAsync(previous.year, previous.month);
                await _services.CalendarSync.EnsureMonthPresentAsync(next.year, next.month);
            }
            catch
            {
                // Network/sync failed – continue with whatever is in the local DB.
            }

            var monthRecord = await _services.CalendarRepository.GetMonthRecordAsync(year, month);
            if (monthRecord is null)
            {
                return;
            }

            MonthTitleNepali = monthRecord.TitleNepali;
            MonthTitleEnglish = monthRecord.TitleEnglish;
            await RefreshNavigationOptionsAsync(year, month);

            var currentMonthDays = await _services.CalendarRepository.GetMonthDaysAsync(year, month);
            var previousMonthDays = await _services.CalendarRepository.GetMonthDaysAsync(previous.year, previous.month);
            var nextMonthDays = await _services.CalendarRepository.GetMonthDaysAsync(next.year, next.month);
            var today = await _services.CalendarRepository.GetTodayAsync();
            SetTodayRecord(today);

            Days.Clear();

            if (currentMonthDays.Count == 0)
            {
                return;
            }

            var leadingDays = GetWeekdayColumnIndex(currentMonthDays[0]);
            foreach (var day in previousMonthDays.Skip(Math.Max(0, previousMonthDays.Count - leadingDays)))
            {
                Days.Add(CreateCell(day, false, today));
            }

            foreach (var day in currentMonthDays)
            {
                Days.Add(CreateCell(day, true, today));
            }

            var trailingIndex = 0;
            while (Days.Count < 42 && trailingIndex < nextMonthDays.Count)
            {
                Days.Add(CreateCell(nextMonthDays[trailingIndex], false, today));
                trailingIndex++;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectCalendarDateAsync(int year, int month, int day)
    {
        await LoadMonthAsync(year, month);
        var match = Days.FirstOrDefault(item => item.Record.BsYear == year && item.Record.BsMonth == month && item.Record.BsDay == day);
        if (match is not null)
        {
            await SelectDayAsync(match);
        }
    }

    public void ClearSelection()
    {
        if (SelectedDay is not null)
        {
            SelectedDay.IsSelected = false;
        }

        SelectedDay = null;
        SelectedNote = string.Empty;
    }

    private async Task LoadPreviousMonthAsync()
    {
        var previous = CalendarSyncService.GetPreviousMonth(_displayYear, _displayMonth);
        ClearSelection();
        await LoadMonthAsync(previous.year, previous.month);
    }

    private async Task LoadNextMonthAsync()
    {
        var next = CalendarSyncService.GetNextMonth(_displayYear, _displayMonth);
        ClearSelection();
        await LoadMonthAsync(next.year, next.month);
    }

    private async Task SelectDayAsync(object? parameter)
    {
        if (parameter is not CalendarDayCellViewModel cell)
        {
            return;
        }

        if (!cell.IsCurrentMonth)
        {
            await LoadMonthAsync(cell.Record.BsYear, cell.Record.BsMonth);
            cell = Days.First(day => day.Record.BsYear == cell.Record.BsYear && day.Record.BsMonth == cell.Record.BsMonth && day.Record.BsDay == cell.Record.BsDay);
        }

        if (SelectedDay is not null)
        {
            SelectedDay.IsSelected = false;
        }

        cell.IsSelected = true;
        SelectedDay = cell;
        SelectedNote = await _services.UserRepository.GetNoteAsync(cell.Record.BsYear, cell.Record.BsMonth, cell.Record.BsDay) ?? string.Empty;
    }

    private async Task SaveNoteAsync()
    {
        if (SelectedDay is null)
        {
            return;
        }

        await _services.UserRepository.SetNoteAsync(SelectedDay.Record.BsYear, SelectedDay.Record.BsMonth, SelectedDay.Record.BsDay, SelectedNote);
    }

    private CalendarDayCellViewModel CreateCell(CalendarDayRecord record, bool isCurrentMonth, CalendarDayRecord? today)
    {
        var isToday = today is not null && today.BsYear == record.BsYear && today.BsMonth == record.BsMonth && today.BsDay == record.BsDay;
        var weekdayColumnIndex = GetWeekdayColumnIndex(record);
        return new CalendarDayCellViewModel
        {
            Record = record,
            IsCurrentMonth = isCurrentMonth,
            IsToday = isToday,
            IsSaturdayColumn = weekdayColumnIndex == 6
        };
    }

    /// <summary>
    /// Returns the 0-based column index (0 = Sunday … 6 = Saturday) for a day record.
    /// Cross-validates the AD date against the stored Nepali weekday name; if they
    /// disagree the Nepali weekday name is used as the authoritative source so that a
    /// single corrupted AdDateIso cannot shift the whole calendar grid.
    /// </summary>
    private static int GetWeekdayColumnIndex(CalendarDayRecord record)
    {
        var nepaliIndex = NepaliWeekdayToIndex(record.NepaliWeekday);

        if (DateOnly.TryParse(record.AdDateIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var adDate))
        {
            var adIndex = (int)adDate.DayOfWeek; // Sunday = 0 … Saturday = 6
            // If both sources agree, great.  If not, trust the Nepali weekday name
            // because it is embedded directly in the day cell's popup text and is
            // harder to corrupt through an off-by-one in the month-level layout.
            return (nepaliIndex < 0 || nepaliIndex == adIndex) ? adIndex : nepaliIndex;
        }

        return nepaliIndex >= 0 ? nepaliIndex : 0;
    }

    private static int NepaliWeekdayToIndex(string nepaliWeekday)
    {
        var w = nepaliWeekday.Trim();
        if (w.Contains("आइत",  StringComparison.Ordinal) || w.Contains("Sunday",    StringComparison.OrdinalIgnoreCase)) return 0;
        if (w.Contains("सोम",  StringComparison.Ordinal) || w.Contains("Monday",    StringComparison.OrdinalIgnoreCase)) return 1;
        if (w.Contains("मंगल", StringComparison.Ordinal) || w.Contains("मङ्गल",    StringComparison.Ordinal)           || w.Contains("Tuesday",   StringComparison.OrdinalIgnoreCase)) return 2;
        if (w.Contains("बुध",  StringComparison.Ordinal) || w.Contains("Wednesday", StringComparison.OrdinalIgnoreCase)) return 3;
        if (w.Contains("बिहि", StringComparison.Ordinal) || w.Contains("बृह",       StringComparison.Ordinal)           || w.Contains("Thursday",  StringComparison.OrdinalIgnoreCase)) return 4;
        if (w.Contains("शुक्र",StringComparison.Ordinal) || w.Contains("Friday",    StringComparison.OrdinalIgnoreCase)) return 5;
        if (w.Contains("शनि",  StringComparison.Ordinal) || w.Contains("Saturday",  StringComparison.OrdinalIgnoreCase)) return 6;
        return -1; // unknown
    }

    private void SetTodayRecord(CalendarDayRecord? today)
    {
        _todayRecord = today;
        RaisePropertyChanged(nameof(TodayBsFullDate));
        RaisePropertyChanged(nameof(TodayAdDateText));
    }

    private async Task RefreshNavigationOptionsAsync(int year, int month)
    {
        var years = await _services.CalendarRepository.GetAvailableBsYearsAsync();
        AvailableYears.Clear();
        foreach (var y in years)
        {
            AvailableYears.Add(y);
        }

        if (!AvailableYears.Contains(year))
        {
            AvailableYears.Add(year);
        }

        var monthRecords = await _services.CalendarRepository.GetAvailableMonthsForYearAsync(year);
        AvailableMonths.Clear();

        if (monthRecords.Count > 0)
        {
            foreach (var m in monthRecords)
            {
                AvailableMonths.Add(new MonthNavigationOption
                {
                    MonthNumber = m.BsMonth,
                    DisplayName = m.TitleNepali
                });
            }
        }
        else
        {
            for (var i = 1; i <= 12; i++)
            {
                AvailableMonths.Add(new MonthNavigationOption
                {
                    MonthNumber = i,
                    DisplayName = NepaliMonthNames[i - 1]
                });
            }
        }

        SelectedNavigationYear = year;
        SelectedNavigationMonth = AvailableMonths.FirstOrDefault(m => m.MonthNumber == month)
                                  ?? AvailableMonths.FirstOrDefault();
    }
}