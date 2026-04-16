using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;

namespace OpenPatro.ViewModels;

/// <summary>
/// Row view model for a single day's bullion prices in the table.
/// </summary>
public sealed class BullionRowViewModel
{
    public BullionRowViewModel(
        string adDate,
        string bsDate,
        string goldHallmarkPerTola,
        string goldTejabiPerTola,
        string silverPerTola,
        string goldHallmarkPer10g,
        string goldTejabiPer10g,
        string silverPer10g,
        bool isToday)
    {
        AdDate = adDate;
        BsDate = bsDate;
        GoldHallmarkPerTola = goldHallmarkPerTola;
        GoldTejabiPerTola = goldTejabiPerTola;
        SilverPerTola = silverPerTola;
        GoldHallmarkPer10g = goldHallmarkPer10g;
        GoldTejabiPer10g = goldTejabiPer10g;
        SilverPer10g = silverPer10g;
        IsToday = isToday;
    }

    public string AdDate { get; }
    public string BsDate { get; }
    public string GoldHallmarkPerTola { get; }
    public string GoldTejabiPerTola { get; }
    public string SilverPerTola { get; }
    public string GoldHallmarkPer10g { get; }
    public string GoldTejabiPer10g { get; }
    public string SilverPer10g { get; }

    /// <summary>True if this row represents today's prices.</summary>
    public bool IsToday { get; }

    /// <summary>Background color for today's row highlight.</summary>
    public string RowBackground => IsToday ? "#0AFFD700" : "Transparent";

    /// <summary>Font weight for today's row.</summary>
    public string RowFontWeight => IsToday ? "SemiBold" : "Normal";
}

/// <summary>
/// ViewModel for the Bullion (Gold/Silver) prices section.
/// Fetches data from the NepaliPatro bullions API and exposes it for display.
/// </summary>
public sealed class BullionViewModel : BindableBase
{
    private const int DefaultItemsPerPage = 120;

    private readonly AppServices _services;
    private readonly List<BullionRowViewModel> _allRows = new();

    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private bool _hasData;

    // Today's summary cards
    private string _todayBsDate = "--";
    private string _todayAdDate = "--";
    private string _goldHallmarkPerTola = "--";
    private string _goldTejabiPerTola = "--";
    private string _silverPerTola = "--";
    private string _goldHallmarkPer10g = "--";
    private string _goldTejabiPer10g = "--";
    private string _silverPer10g = "--";

    // Change indicators vs previous day
    private string _goldHallmarkChange = "";
    private string _goldTejabiChange = "";
    private string _silverChange = "";
    private bool _goldHallmarkUp;
    private bool _goldTejabiUp;
    private bool _silverUp;
    private bool _goldHallmarkDown;
    private bool _goldTejabiDown;
    private bool _silverDown;
    private string _selectedRangeKey = "30D";
    private string _rangeSummary = "Showing latest 30 days";
    private string _rowWindowSummary = string.Empty;
    private int _dataVersion;
    private int _itemsPerPage = DefaultItemsPerPage;
    private int _currentPage = 1;
    private int _totalPages;

    public BullionViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        NextPageCommand = new RelayCommand(GoToNextPage, () => CanGoToNextPage && !IsBusy);
        PreviousPageCommand = new RelayCommand(GoToPreviousPage, () => CanGoToPreviousPage && !IsBusy);
    }

    public ICommand RefreshCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    /// <summary>Historical rows, newest first.</summary>
    public ObservableCollection<BullionRowViewModel> Rows { get; } = new();

    public IReadOnlyList<BullionRowViewModel> ChartRows => _allRows;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
                ((RelayCommand)NextPageCommand).NotifyCanExecuteChanged();
                ((RelayCommand)PreviousPageCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    // ── Today's summary ──

    public string TodayBsDate
    {
        get => _todayBsDate;
        private set => SetProperty(ref _todayBsDate, value);
    }

    public string TodayAdDate
    {
        get => _todayAdDate;
        private set => SetProperty(ref _todayAdDate, value);
    }

    public string GoldHallmarkPerTola
    {
        get => _goldHallmarkPerTola;
        private set => SetProperty(ref _goldHallmarkPerTola, value);
    }

    public string GoldTejabiPerTola
    {
        get => _goldTejabiPerTola;
        private set => SetProperty(ref _goldTejabiPerTola, value);
    }

    public string SilverPerTola
    {
        get => _silverPerTola;
        private set => SetProperty(ref _silverPerTola, value);
    }

    public string GoldHallmarkPer10g
    {
        get => _goldHallmarkPer10g;
        private set => SetProperty(ref _goldHallmarkPer10g, value);
    }

    public string GoldTejabiPer10g
    {
        get => _goldTejabiPer10g;
        private set => SetProperty(ref _goldTejabiPer10g, value);
    }

    public string SilverPer10g
    {
        get => _silverPer10g;
        private set => SetProperty(ref _silverPer10g, value);
    }

    // ── Change indicators ──

    public string GoldHallmarkChange
    {
        get => _goldHallmarkChange;
        private set => SetProperty(ref _goldHallmarkChange, value);
    }

    public string GoldTejabiChange
    {
        get => _goldTejabiChange;
        private set => SetProperty(ref _goldTejabiChange, value);
    }

    public string SilverChange
    {
        get => _silverChange;
        private set => SetProperty(ref _silverChange, value);
    }

    public bool GoldHallmarkUp
    {
        get => _goldHallmarkUp;
        private set => SetProperty(ref _goldHallmarkUp, value);
    }

    public bool GoldTejabiUp
    {
        get => _goldTejabiUp;
        private set => SetProperty(ref _goldTejabiUp, value);
    }

    public bool SilverUp
    {
        get => _silverUp;
        private set => SetProperty(ref _silverUp, value);
    }

    public bool GoldHallmarkDown
    {
        get => _goldHallmarkDown;
        private set => SetProperty(ref _goldHallmarkDown, value);
    }

    public bool GoldTejabiDown
    {
        get => _goldTejabiDown;
        private set => SetProperty(ref _goldTejabiDown, value);
    }

    public bool SilverDown
    {
        get => _silverDown;
        private set => SetProperty(ref _silverDown, value);
    }

    public string SelectedRangeKey
    {
        get => _selectedRangeKey;
        private set => SetProperty(ref _selectedRangeKey, value);
    }

    public string RangeSummary
    {
        get => _rangeSummary;
        private set => SetProperty(ref _rangeSummary, value);
    }

    public int DataVersion
    {
        get => _dataVersion;
        private set => SetProperty(ref _dataVersion, value);
    }

    public int ItemsPerPage
    {
        get => _itemsPerPage;
        private set => SetProperty(ref _itemsPerPage, value);
    }

    public string RowWindowSummary
    {
        get => _rowWindowSummary;
        private set => SetProperty(ref _rowWindowSummary, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => SetProperty(ref _totalPages, value);
    }

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public string CurrentPageLabel => TotalPages <= 0 ? string.Empty : $"Page {CurrentPage:N0} of {TotalPages:N0}";

    public void SetItemsPerPage(int itemsPerPage)
    {
        var normalized = NormalizeItemsPerPage(itemsPerPage);
        if (normalized == ItemsPerPage)
        {
            return;
        }

        ItemsPerPage = normalized;
        CurrentPage = 1;
        TotalPages = Math.Max(1, (int)Math.Ceiling(_allRows.Count / (double)ItemsPerPage));
        UpdateVisibleRows();
        RefreshPaginationCommands();
    }

    private void RefreshPaginationCommands()
    {
        RaisePropertyChanged(nameof(CanGoToPreviousPage));
        RaisePropertyChanged(nameof(CanGoToNextPage));
        RaisePropertyChanged(nameof(CurrentPageLabel));
        ((RelayCommand)NextPageCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).NotifyCanExecuteChanged();
    }

    private void GoToPreviousPage()
    {
        if (!CanGoToPreviousPage || IsBusy)
        {
            return;
        }

        CurrentPage--;
        UpdateVisibleRows();
    }

    private void GoToNextPage()
    {
        if (!CanGoToNextPage || IsBusy)
        {
            return;
        }

        CurrentPage++;
        UpdateVisibleRows();
    }

    public async Task ApplyRangeAsync(string? rangeKey)
    {
        var normalized = NormalizeRangeKey(rangeKey);
        if (string.Equals(SelectedRangeKey, normalized, StringComparison.Ordinal) && HasData)
        {
            return;
        }

        SelectedRangeKey = normalized;
        await RefreshAsync();
    }

    /// <summary>
    /// Loads data only once; subsequent calls are no-ops unless forced via RefreshCommand.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (HasData || IsBusy)
        {
            return;
        }

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            var includeAllHistory = string.Equals(SelectedRangeKey, "ALL", StringComparison.Ordinal);
            var fromDate = includeAllHistory ? (DateOnly?)null : ResolveFromDateForRange(SelectedRangeKey);

            var response = await _services.Bullion.FetchAsync(
                fromDate: fromDate,
                includeAllHistory: includeAllHistory,
                cancellationToken: cts.Token);

            if (response is null || response.Entries.Count == 0)
            {
                ErrorMessage = "No bullion data returned by the server.";
                RangeSummary = "No data available for selected range.";
                return;
            }

            // Sort entries by date descending (newest first)
            var sorted = response.Entries
                .Select(kvp =>
                {
                    DateOnly.TryParseExact(kvp.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date);
                    return (Date: date, AdKey: kvp.Key, Entry: kvp.Value);
                })
                .OrderByDescending(x => x.Date)
                .ToList();

            if (sorted.Count == 0)
            {
                ErrorMessage = "Could not parse bullion data.";
                RangeSummary = "No data available for selected range.";
                return;
            }

            var oldest = sorted[^1].Date;
            var newest = sorted[0].Date;
            var oldestLabel = oldest == DateOnly.MinValue ? sorted[^1].AdKey : oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var newestLabel = newest == DateOnly.MinValue ? sorted[0].AdKey : newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            RangeSummary = string.Equals(SelectedRangeKey, "ALL", StringComparison.Ordinal)
                ? $"Showing full history: {sorted.Count:N0} days ({oldestLabel} → {newestLabel})"
                : $"Showing {sorted.Count:N0} days ({oldestLabel} → {newestLabel})";

            // Today's entry is the most recent one
            var todayEntry = sorted[0];
            var todayDate = todayEntry.Date;

            TodayBsDate = todayEntry.Entry.Bs;
            TodayAdDate = todayDate == DateOnly.MinValue
                ? todayEntry.AdKey
                : todayDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

            GoldHallmarkPerTola = FormatPrice(todayEntry.Entry.GoldHallmarkPerTola);
            GoldTejabiPerTola = FormatPrice(todayEntry.Entry.GoldTejabiPerTola);
            SilverPerTola = FormatPrice(todayEntry.Entry.SilverPerTola);
            GoldHallmarkPer10g = FormatPrice(todayEntry.Entry.GoldHallmarkPer10g);
            GoldTejabiPer10g = FormatPrice(todayEntry.Entry.GoldTejabiPer10g);
            SilverPer10g = FormatPrice(todayEntry.Entry.SilverPer10g);

            // Compute change vs previous day
            if (sorted.Count >= 2)
            {
                var prevEntry = sorted[1].Entry;
                SetChangeIndicator(
                    todayEntry.Entry.GoldHallmarkPerTola,
                    prevEntry.GoldHallmarkPerTola,
                    v => GoldHallmarkChange = v,
                    v => GoldHallmarkUp = v,
                    v => GoldHallmarkDown = v);

                SetChangeIndicator(
                    todayEntry.Entry.GoldTejabiPerTola,
                    prevEntry.GoldTejabiPerTola,
                    v => GoldTejabiChange = v,
                    v => GoldTejabiUp = v,
                    v => GoldTejabiDown = v);

                SetChangeIndicator(
                    todayEntry.Entry.SilverPerTola,
                    prevEntry.SilverPerTola,
                    v => SilverChange = v,
                    v => SilverUp = v,
                    v => SilverDown = v);
            }
            else
            {
                GoldHallmarkChange = "";
                GoldTejabiChange = "";
                SilverChange = "";
                GoldHallmarkUp = GoldHallmarkDown = false;
                GoldTejabiUp = GoldTejabiDown = false;
                SilverUp = SilverDown = false;
            }

            // Build table rows
            var todayAdKey = todayEntry.AdKey;
            var newRows = sorted.Select(item =>
            {
                var isToday = string.Equals(item.AdKey, todayAdKey, StringComparison.Ordinal)
                              && item.AdKey == sorted[0].AdKey;
                return new BullionRowViewModel(
                    adDate: item.Date == DateOnly.MinValue
                        ? item.AdKey
                        : item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    bsDate: item.Entry.Bs,
                    goldHallmarkPerTola: FormatPrice(item.Entry.GoldHallmarkPerTola),
                    goldTejabiPerTola: FormatPrice(item.Entry.GoldTejabiPerTola),
                    silverPerTola: FormatPrice(item.Entry.SilverPerTola),
                    goldHallmarkPer10g: FormatPrice(item.Entry.GoldHallmarkPer10g),
                    goldTejabiPer10g: FormatPrice(item.Entry.GoldTejabiPer10g),
                    silverPer10g: FormatPrice(item.Entry.SilverPer10g),
                    isToday: isToday);
            }).ToList();

            _allRows.Clear();
            _allRows.AddRange(newRows);

            CurrentPage = 1;
            TotalPages = Math.Max(1, (int)Math.Ceiling(_allRows.Count / (double)ItemsPerPage));
            RefreshPaginationCommands();

            UpdateVisibleRows();
            HasData = true;
            DataVersion++;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Bullion data request timed out. Please try again.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load bullion data: {ex.Message}";
            Debug.WriteLine($"BullionViewModel fetch error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void SetChangeIndicator(
        decimal current,
        decimal previous,
        Action<string> setChange,
        Action<bool> setUp,
        Action<bool> setDown)
    {
        var diff = current - previous;
        if (diff > 0)
        {
            setChange($"+{diff:N0}");
            setUp(true);
            setDown(false);
        }
        else if (diff < 0)
        {
            setChange($"{diff:N0}");
            setUp(false);
            setDown(true);
        }
        else
        {
            setChange("—");
            setUp(false);
            setDown(false);
        }
    }

    private static string FormatPrice(decimal value)
    {
        return value == 0 ? "--" : value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string NormalizeRangeKey(string? key)
    {
        return key?.Trim().ToUpperInvariant() switch
        {
            "30D" => "30D",
            "90D" => "90D",
            "6M" => "6M",
            "1Y" => "1Y",
            "ALL" => "ALL",
            _ => "30D"
        };
    }

    private static int NormalizeItemsPerPage(int value)
    {
        return value switch
        {
            <= 25 => 25,
            <= 50 => 50,
            <= 100 => 100,
            <= 200 => 200,
            _ => 500
        };
    }

    private static DateOnly ResolveFromDateForRange(string rangeKey)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return rangeKey switch
        {
            "90D" => today.AddDays(-90),
            "6M" => today.AddMonths(-6),
            "1Y" => today.AddYears(-1),
            _ => today.AddDays(-30)
        };
    }

    private void UpdateVisibleRows()
    {
        if (_allRows.Count == 0)
        {
            ReplaceCollection(Rows, Array.Empty<BullionRowViewModel>());
            RowWindowSummary = string.Empty;
            CurrentPage = 1;
            TotalPages = 1;
            RefreshPaginationCommands();
            return;
        }

        if (CurrentPage < 1) CurrentPage = 1;
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;

        var skip = (CurrentPage - 1) * ItemsPerPage;
        var visibleRows = _allRows.Skip(skip).Take(ItemsPerPage).ToList();
        ReplaceCollection(Rows, visibleRows);

        var start = skip + 1;
        var end = skip + visibleRows.Count;
        RowWindowSummary = $"Showing {start:N0}–{end:N0} of {_allRows.Count:N0} rows";
        RefreshPaginationCommands();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IList<T> source)
    {
        int i = 0;
        foreach (var item in source)
        {
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    target[i] = item;
                }
            }
            else
            {
                target.Add(item);
            }
            i++;
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
