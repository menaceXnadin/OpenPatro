using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Models;

namespace OpenPatro.ViewModels;

public sealed class StockMarketViewModel : BindableBase
{
    private readonly AppServices _services;

    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _marketStatus = "--";
    private string _lastUpdated = string.Empty;
    private string _totalTurnover = "--";
    private string _totalTransactions = "--";
    private string _totalTradedShares = "--";
    private string _advanceDecline = "--";
    private string _nepseValue = "--";
    private string _nepseChange = "--";
    private string _marketBreadth = "--";
    private string _positiveCircuit = "--";
    private string _negativeCircuit = "--";
    private string _demandCount = "--";
    private string _supplyCount = "--";
    private string _liveCompanyCount = "--";
    private string _demandSummary = "Demand entries: --";
    private string _supplySummary = "Supply entries: --";
    private bool _isMarketOpen;
    private string _selectedTopStocksTab = "TopGainers";
    private string _scriptsTraded = "--";
    private string _marketCap = "--";
    private string _floatMarketCap = "--";
    private int _advancedCount;
    private int _declinedCount;
    private int _unchangedCount;
    private int _positiveCircuitCount;
    private int _negativeCircuitCount;
    private string _marketStatusBadgeColor = "#808080";
    private string _sectorSortColumn = "Name";
    private bool _sectorSortAscending = true;
    private string _topStocksSortColumn = "Metric";
    private bool _topStocksSortAscending = false;
    private bool _hasUserSortedTopStocks = false;
    private string _liveSortColumn = "Symbol";
    private bool _liveSortAscending = true;

    public StockMarketViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);

        // Keep at least 4 entries for index-based XAML bindings (MajorIndices[0..3]).
        MajorIndices.Add(CreatePlaceholderIndex());
        MajorIndices.Add(CreatePlaceholderIndex());
        MajorIndices.Add(CreatePlaceholderIndex());
        MajorIndices.Add(CreatePlaceholderIndex());
    }

    public ICommand RefreshCommand { get; }

    public ObservableCollection<IndexSnapshotViewModel> MajorIndices { get; } = new();

    public ObservableCollection<IndexSnapshotViewModel> SectorIndices { get; } = new();

    public ObservableCollection<MarketSummaryRowViewModel> MarketSummaryRows { get; } = new();

    public ObservableCollection<MarketMoverRowViewModel> TopGainers { get; } = new();

    public ObservableCollection<MarketMoverRowViewModel> TopLosers { get; } = new();

    public ObservableCollection<MarketMoverRowViewModel> TopTurnover { get; } = new();

    public ObservableCollection<MarketMoverRowViewModel> TopTradedShares { get; } = new();

    public ObservableCollection<MarketMoverRowViewModel> TopTransactions { get; } = new();

    public ObservableCollection<LiveCompanyRowViewModel> LiveCompanies { get; } = new();

    public ObservableCollection<LiveCompanyRowViewModel> FilteredLiveCompanies { get; } = new();

    private string _liveMarketSearchText = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string MarketStatus
    {
        get => _marketStatus;
        private set => SetProperty(ref _marketStatus, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }

    public string TotalTurnover
    {
        get => _totalTurnover;
        private set => SetProperty(ref _totalTurnover, value);
    }

    public string TotalTransactions
    {
        get => _totalTransactions;
        private set => SetProperty(ref _totalTransactions, value);
    }

    public string TotalTradedShares
    {
        get => _totalTradedShares;
        private set => SetProperty(ref _totalTradedShares, value);
    }

    public string AdvanceDecline
    {
        get => _advanceDecline;
        private set => SetProperty(ref _advanceDecline, value);
    }

    public string NepseValue
    {
        get => _nepseValue;
        private set => SetProperty(ref _nepseValue, value);
    }

    public string NepseChange
    {
        get => _nepseChange;
        private set => SetProperty(ref _nepseChange, value);
    }

    public string MarketBreadth
    {
        get => _marketBreadth;
        private set => SetProperty(ref _marketBreadth, value);
    }

    public string PositiveCircuit
    {
        get => _positiveCircuit;
        private set => SetProperty(ref _positiveCircuit, value);
    }

    public string NegativeCircuit
    {
        get => _negativeCircuit;
        private set => SetProperty(ref _negativeCircuit, value);
    }

    public string DemandCount
    {
        get => _demandCount;
        private set => SetProperty(ref _demandCount, value);
    }

    public string SupplyCount
    {
        get => _supplyCount;
        private set => SetProperty(ref _supplyCount, value);
    }

    public string LiveCompanyCount
    {
        get => _liveCompanyCount;
        private set => SetProperty(ref _liveCompanyCount, value);
    }

    public string DemandSummary
    {
        get => _demandSummary;
        private set => SetProperty(ref _demandSummary, value);
    }

    public string SupplySummary
    {
        get => _supplySummary;
        private set => SetProperty(ref _supplySummary, value);
    }

    public bool IsMarketOpen
    {
        get => _isMarketOpen;
        private set => SetProperty(ref _isMarketOpen, value);
    }

    public string SelectedTopStocksTab
    {
        get => _selectedTopStocksTab;
        set
        {
            if (SetProperty(ref _selectedTopStocksTab, value))
            {
                RaisePropertyChanged(nameof(IsTopGainersSelected));
                RaisePropertyChanged(nameof(IsTopLosersSelected));
                RaisePropertyChanged(nameof(IsTopTurnoverSelected));
                RaisePropertyChanged(nameof(IsTopVolumeSelected));
                RaisePropertyChanged(nameof(IsTopTransactionsSelected));
                RaisePropertyChanged(nameof(CurrentTopStocksList));
            }
        }
    }

    public bool IsTopGainersSelected => SelectedTopStocksTab == "TopGainers";
    public bool IsTopLosersSelected => SelectedTopStocksTab == "TopLosers";
    public bool IsTopTurnoverSelected => SelectedTopStocksTab == "TopTurnover";
    public bool IsTopVolumeSelected => SelectedTopStocksTab == "TopVolume";
    public bool IsTopTransactionsSelected => SelectedTopStocksTab == "TopTransactions";

    public ObservableCollection<MarketMoverRowViewModel> CurrentTopStocksList
    {
        get
        {
            return SelectedTopStocksTab switch
            {
                "TopLosers" => TopLosers,
                "TopTurnover" => TopTurnover,
                "TopVolume" => TopTradedShares,
                "TopTransactions" => TopTransactions,
                _ => TopGainers
            };
        }
    }

    public string ScriptsTraded
    {
        get => _scriptsTraded;
        private set => SetProperty(ref _scriptsTraded, value);
    }

    public string MarketCap
    {
        get => _marketCap;
        private set => SetProperty(ref _marketCap, value);
    }

    public string FloatMarketCap
    {
        get => _floatMarketCap;
        private set => SetProperty(ref _floatMarketCap, value);
    }

    public int AdvancedCount
    {
        get => _advancedCount;
        private set => SetProperty(ref _advancedCount, value);
    }

    public int DeclinedCount
    {
        get => _declinedCount;
        private set => SetProperty(ref _declinedCount, value);
    }

    public int UnchangedCount
    {
        get => _unchangedCount;
        private set => SetProperty(ref _unchangedCount, value);
    }

    public int PositiveCircuitCount
    {
        get => _positiveCircuitCount;
        private set => SetProperty(ref _positiveCircuitCount, value);
    }

    public int NegativeCircuitCount
    {
        get => _negativeCircuitCount;
        private set => SetProperty(ref _negativeCircuitCount, value);
    }

    public string MarketStatusBadgeColor
    {
        get => _marketStatusBadgeColor;
        private set => SetProperty(ref _marketStatusBadgeColor, value);
    }

    public string SectorSortColumn => _sectorSortColumn;

    public bool IsSectorSortAscending => _sectorSortAscending;

    public string TopStocksSortColumn => _topStocksSortColumn;

    public bool IsTopStocksSortAscending => _topStocksSortAscending;

    public string LiveSortColumn => _liveSortColumn;

    public bool IsLiveSortAscending => _liveSortAscending;

    public async Task InitializeAsync()
    {
        if (TopGainers.Count > 0 || TopLosers.Count > 0 || LiveCompanies.Count > 0)
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
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(25));
            var response = await _services.ShareHubNepse.FetchHomePageDataAsync(cts.Token);
            if (response is null)
            {
                ErrorMessage = "No stock market data returned by the server.";
                return;
            }

            MapResponse(response);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Stock market request timed out. Please try refresh again.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load stock market data: {ex.Message}";
            Debug.WriteLine($"StockMarketViewModel fetch error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void MapResponse(StockMarketHomeData response)
    {
        var status = response.MarketStatus?.Status?.Trim();
        MarketStatus = string.IsNullOrWhiteSpace(status) ? "UNKNOWN" : status.ToUpperInvariant();
        IsMarketOpen = string.Equals(MarketStatus, "OPEN", StringComparison.OrdinalIgnoreCase);
        MarketStatusBadgeColor = IsMarketOpen ? "#22C55E" : "#EF4444";

        LastUpdated = TryFormatUtc(response.MarketStatus?.Time);

        var summary = response.MarketSummary ?? new List<MarketSummaryEntry>();
        ReplaceCollection(MarketSummaryRows, summary.Select(s => new MarketSummaryRowViewModel(
            NormalizeWhitespace(s.Name),
            s.Name.Contains("Rs", StringComparison.OrdinalIgnoreCase)
                ? FormatCurrency(s.Value)
                : FormatCount(s.Value))));

        var turnover = TryGetSummaryValue(summary, key => key.Contains("turnover", StringComparison.Ordinal));
        var totalTransactions = TryGetSummaryValue(summary, key => key.Contains("transactions", StringComparison.Ordinal));
        var tradedShares = TryGetSummaryValue(summary, key => key.Contains("tradedshares", StringComparison.Ordinal));
        var scriptsTraded = TryGetSummaryValue(summary, key =>
            key.Contains("traded", StringComparison.Ordinal) &&
            (key.Contains("scrip", StringComparison.Ordinal) || key.Contains("script", StringComparison.Ordinal)));
        var marketCap = TryGetSummaryValue(summary, key =>
            (key.Contains("marketcap", StringComparison.Ordinal) || key.Contains("marketcapital", StringComparison.Ordinal)) &&
            !key.Contains("float", StringComparison.Ordinal));
        var floatMarketCap = TryGetSummaryValue(summary, key =>
            key.Contains("float", StringComparison.Ordinal) &&
            (key.Contains("marketcap", StringComparison.Ordinal) || key.Contains("marketcapital", StringComparison.Ordinal)));

        TotalTurnover = FormatLargeNumber(turnover);
        TotalTransactions = FormatLargeNumber(totalTransactions);
        TotalTradedShares = FormatLargeNumber(tradedShares);
        ScriptsTraded = FormatCount(scriptsTraded);
        MarketCap = FormatLargeNumber(marketCap);
        FloatMarketCap = FormatLargeNumber(floatMarketCap);

        var stockSummary = response.StockSummary;
        if (stockSummary is not null)
        {
            AdvancedCount = stockSummary.Advanced;
            DeclinedCount = stockSummary.Declined;
            UnchangedCount = stockSummary.Unchanged;
            PositiveCircuitCount = stockSummary.PositiveCircuit;
            NegativeCircuitCount = stockSummary.NegativeCircuit;
            AdvanceDecline = $"Adv {stockSummary.Advanced} | Dec {stockSummary.Declined} | Unch {stockSummary.Unchanged}";
            MarketBreadth = stockSummary.Advanced >= stockSummary.Declined ? "Bullish Breadth" : "Bearish Breadth";
            PositiveCircuit = stockSummary.PositiveCircuit.ToString(CultureInfo.InvariantCulture);
            NegativeCircuit = stockSummary.NegativeCircuit.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            AdvancedCount = 0;
            DeclinedCount = 0;
            UnchangedCount = 0;
            PositiveCircuitCount = 0;
            NegativeCircuitCount = 0;
            AdvanceDecline = "--";
            MarketBreadth = "--";
            PositiveCircuit = "--";
            NegativeCircuit = "--";
        }

        var nepse = response.Indices?.FirstOrDefault(x => string.Equals(x.Symbol, "NEPSE", StringComparison.OrdinalIgnoreCase));
        if (nepse is not null)
        {
            NepseValue = nepse.CurrentValue.ToString("N2", CultureInfo.InvariantCulture);
            NepseChange = $"{nepse.Change:+0.##;-0.##;0} ({nepse.ChangePercent:+0.##;-0.##;0}%)";
        }
        else
        {
            NepseValue = "--";
            NepseChange = "--";
        }

        ReplaceFixedCollection(MajorIndices, (response.Indices ?? new List<MarketIndexInfo>())
            .Select(i => new IndexSnapshotViewModel(
                NormalizeWhitespace(i.Name),
                i.Symbol,
                i.CurrentValue.ToString("N2", CultureInfo.InvariantCulture),
                $"{i.Change:+0.##;-0.##;0}",
                i.ChangePercent)),
            4,
            CreatePlaceholderIndex);

        ReplaceCollection(SectorIndices, (response.SubIndices ?? new List<MarketIndexInfo>())
            .Select(i => new IndexSnapshotViewModel(
                NormalizeWhitespace(i.Name),
                i.Symbol,
                i.CurrentValue.ToString("N2", CultureInfo.InvariantCulture),
                $"{i.Change:+0.##;-0.##;0}",
                i.ChangePercent,
                i.CurrentValue)));

        ReplaceCollection(TopGainers, (response.TopGainers ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"+{m.Change:0.##}",
                $"{m.ChangePercent:+0.##;-0.##;0}%",
                m.LastTradedPrice,
                m.Change,
                m.ChangePercent,
                "▲",
                m.LogoPath)));

        ReplaceCollection(TopLosers, (response.TopLosers ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:0.##}",
                $"{m.ChangePercent:+0.##;-0.##;0}%",
                m.LastTradedPrice,
                m.Change,
                m.ChangePercent,
                "▼",
                m.LogoPath)));

        ReplaceCollection(TopTurnover, (response.TopTurnover ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:+0.##;-0.##;0}",
                FormatLargeNumber(m.Turnover),
                m.LastTradedPrice,
                m.Change,
                m.Turnover,
                "Rs",
                m.LogoPath)));

        ReplaceCollection(TopTradedShares, (response.TopTradedShares ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:+0.##;-0.##;0}",
                FormatLargeNumber(m.SharesTraded),
                m.LastTradedPrice,
                m.Change,
                m.SharesTraded,
                "Qty",
                m.LogoPath)));

        ReplaceCollection(TopTransactions, (response.TopTransactions ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:+0.##;-0.##;0}",
                FormatCount(m.Transactions),
                m.LastTradedPrice,
                m.Change,
                m.Transactions,
                "Tx",
                m.LogoPath)));

        ReplaceCollection(LiveCompanies, (response.LiveCompanyData ?? new List<LiveCompanyDataInfo>())
            .Select(c => new LiveCompanyRowViewModel(
                c.Symbol,
                NormalizeWhitespace(c.SecurityName),
                NormalizeWhitespace(c.Sector ?? string.Empty),
                c.OpenPrice.ToString("N2", CultureInfo.InvariantCulture),
                c.HighPrice.ToString("N2", CultureInfo.InvariantCulture),
                c.LowPrice.ToString("N2", CultureInfo.InvariantCulture),
                c.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                c.PreviousClose.ToString("N2", CultureInfo.InvariantCulture),
                $"{c.PercentageChange:+0.##;-0.##;0}%",
                c.TotalTradeQuantity.ToString("N2", CultureInfo.InvariantCulture),
                c.TotalTradeValue.ToString("N2", CultureInfo.InvariantCulture),
                c.TotalTransactions.ToString("N0", CultureInfo.InvariantCulture),
                c.OpenPrice,
                c.HighPrice,
                c.LowPrice,
                c.LastTradedPrice,
                c.PreviousClose,
                c.PercentageChange,
                c.TotalTradeQuantity,
                c.TotalTradeValue,
                c.TotalTransactions,
                c.LogoPath)));

        ApplySectorSort();
        
        // Only apply top stocks sort if user has manually sorted
        // Otherwise, keep the API's original order (which is already correct)
        if (_hasUserSortedTopStocks)
        {
            ApplyTopStocksSort();
        }
        
        ApplyLiveCompaniesSort();

        DemandCount = GetJsonArrayCount(response.Demand).ToString(CultureInfo.InvariantCulture);
        SupplyCount = GetJsonArrayCount(response.Supply).ToString(CultureInfo.InvariantCulture);
        LiveCompanyCount = LiveCompanies.Count.ToString("N0", CultureInfo.InvariantCulture);
        DemandSummary = $"Demand entries: {DemandCount}";
        SupplySummary = $"Supply entries: {SupplyCount}";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        var sourceList = source.ToList();
        
        // Optimization 1: Skip if collections are identical
        if (target.Count == sourceList.Count && target.SequenceEqual(sourceList))
        {
            return;
        }

        // Optimization 2: Update in-place when possible to avoid full Clear/Add cycle
        int i = 0;
        foreach (var item in sourceList)
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

        // Remove excess items from the end
        while (target.Count > sourceList.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static void ReplaceFixedCollection<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source,
        int minimumCount,
        Func<T> placeholderFactory)
    {
        var items = source.ToList();
        while (items.Count < minimumCount)
        {
            items.Add(placeholderFactory());
        }

        while (target.Count < items.Count)
        {
            target.Add(placeholderFactory());
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var i = 0; i < items.Count; i++)
        {
            target[i] = items[i];
        }
    }

    private static IndexSnapshotViewModel CreatePlaceholderIndex()
    {
        return new IndexSnapshotViewModel("--", "--", "--", "--", 0m);
    }

    private static string TryFormatUtc(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "Updated time unavailable";
        }

        if (!DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return $"Last updated: {input}";
        }

        return $"Last updated: {parsed.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private static string NormalizeWhitespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var cleaned = input.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int GetJsonArrayCount(System.Text.Json.JsonElement element)
    {
        return element.ValueKind == System.Text.Json.JsonValueKind.Array
            ? element.GetArrayLength()
            : 0;
    }

    private static decimal? TryGetSummaryValue(IEnumerable<MarketSummaryEntry> summary, Func<string, bool> predicate)
    {
        foreach (var entry in summary)
        {
            if (entry is null)
            {
                continue;
            }

            var normalizedName = NormalizeSummaryKey(entry.Name);
            if (predicate(normalizedName))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static string NormalizeSummaryKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[input.Length];
        var index = 0;
        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = char.ToLowerInvariant(ch);
            }
        }

        return index == 0 ? string.Empty : new string(buffer[..index]);
    }

    private static string FormatCurrency(decimal? value)
    {
        return value.HasValue
            ? $"Rs {value.Value:N2}"
            : "--";
    }

    private static string FormatCurrency(decimal value)
    {
        return $"Rs {value:N2}";
    }

    private static string FormatCount(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("N0", CultureInfo.InvariantCulture)
            : "--";
    }

    private static string FormatCount(decimal value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatLargeNumber(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("N0", CultureInfo.InvariantCulture)
            : "--";
    }

    private static string FormatLargeNumber(decimal value)
    {
        // Show full numbers with comma separators instead of abbreviations
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    public void SortSectorBy(string column)
    {
        if (string.Equals(_sectorSortColumn, column, StringComparison.Ordinal))
        {
            _sectorSortAscending = !_sectorSortAscending;
        }
        else
        {
            _sectorSortColumn = column;
            _sectorSortAscending = true;
        }

        ApplySectorSort();
        RaisePropertyChanged(nameof(SectorSortColumn));
        RaisePropertyChanged(nameof(IsSectorSortAscending));
    }

    public void SortTopStocksBy(string column)
    {
        _hasUserSortedTopStocks = true;
        
        if (string.Equals(_topStocksSortColumn, column, StringComparison.Ordinal))
        {
            _topStocksSortAscending = !_topStocksSortAscending;
        }
        else
        {
            _topStocksSortColumn = column;
            _topStocksSortAscending = true;
        }

        ApplyTopStocksSort();
        RaisePropertyChanged(nameof(TopStocksSortColumn));
        RaisePropertyChanged(nameof(IsTopStocksSortAscending));
    }

    public void SortLiveCompaniesBy(string column)
    {
        if (string.Equals(_liveSortColumn, column, StringComparison.Ordinal))
        {
            _liveSortAscending = !_liveSortAscending;
        }
        else
        {
            _liveSortColumn = column;
            _liveSortAscending = true;
        }

        ApplyLiveCompaniesSort();
        RaisePropertyChanged(nameof(LiveSortColumn));
        RaisePropertyChanged(nameof(IsLiveSortAscending));
    }

    private void ApplySectorSort()
    {
        var sortedList = SectorIndices.ToList();
        
        Comparison<IndexSnapshotViewModel> comparison = _sectorSortColumn switch
        {
            "Value" => (a, b) => a.CurrentValueNumeric.CompareTo(b.CurrentValueNumeric),
            "ChangePercent" => (a, b) => a.ChangePercentNumeric.CompareTo(b.ChangePercentNumeric),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
        };

        // Reverse comparison for descending sort
        if (!_sectorSortAscending)
        {
            var originalComparison = comparison;
            comparison = (a, b) => originalComparison(b, a);
        }

        sortedList.Sort(comparison);
        ReplaceCollection(SectorIndices, sortedList);
    }

    private void ApplyTopStocksSort()
    {
        SortTopCollection(TopGainers);
        SortTopCollection(TopLosers);
        SortTopCollection(TopTurnover);
        SortTopCollection(TopTradedShares);
        SortTopCollection(TopTransactions);
        RaisePropertyChanged(nameof(CurrentTopStocksList));
    }

    private void SortTopCollection(ObservableCollection<MarketMoverRowViewModel> target)
    {
        var sortedList = target.ToList();
        
        Comparison<MarketMoverRowViewModel> comparison = _topStocksSortColumn switch
        {
            "Ltp" => (a, b) => a.LastTradedPriceValue.CompareTo(b.LastTradedPriceValue),
            "Change" => (a, b) => a.ChangeValue.CompareTo(b.ChangeValue),
            "Metric" => (a, b) => a.MetricValue.CompareTo(b.MetricValue),
            _ => (a, b) => string.Compare(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase)
        };

        // Reverse comparison for descending sort
        if (!_topStocksSortAscending)
        {
            var originalComparison = comparison;
            comparison = (a, b) => originalComparison(b, a);
        }

        sortedList.Sort(comparison);
        ReplaceCollection(target, sortedList);
    }

    private void ApplyLiveCompaniesSort()
    {
        // For large collections, use List.Sort which is more efficient than LINQ OrderBy
        var sortedList = LiveCompanies.ToList();
        
        Comparison<LiveCompanyRowViewModel> comparison = _liveSortColumn switch
        {
            "Logo" => (a, b) => string.Compare(a.LogoUrl, b.LogoUrl, StringComparison.OrdinalIgnoreCase),
            "Name" => (a, b) => string.Compare(a.SecurityName, b.SecurityName, StringComparison.OrdinalIgnoreCase),
            "Sector" => (a, b) => string.Compare(a.Sector, b.Sector, StringComparison.OrdinalIgnoreCase),
            "Open" => (a, b) => a.OpenPriceValue.CompareTo(b.OpenPriceValue),
            "High" => (a, b) => a.HighPriceValue.CompareTo(b.HighPriceValue),
            "Low" => (a, b) => a.LowPriceValue.CompareTo(b.LowPriceValue),
            "Ltp" => (a, b) => a.LastTradedPriceValue.CompareTo(b.LastTradedPriceValue),
            "Prev" => (a, b) => a.PreviousCloseValue.CompareTo(b.PreviousCloseValue),
            "ChangePercent" => (a, b) => a.ChangePercentValue.CompareTo(b.ChangePercentValue),
            "Volume" => (a, b) => a.QuantityValue.CompareTo(b.QuantityValue),
            "Turnover" => (a, b) => a.TurnoverValue.CompareTo(b.TurnoverValue),
            "Trades" => (a, b) => a.TransactionsValue.CompareTo(b.TransactionsValue),
            _ => (a, b) => string.Compare(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase)
        };

        // Reverse comparison for descending sort
        if (!_liveSortAscending)
        {
            var originalComparison = comparison;
            comparison = (a, b) => originalComparison(b, a);
        }

        sortedList.Sort(comparison);
        ReplaceCollection(LiveCompanies, sortedList);
        ApplyLiveMarketFilter();
    }

    public void FilterLiveMarket(string searchText)
    {
        _liveMarketSearchText = searchText?.Trim() ?? string.Empty;
        ApplyLiveMarketFilter();
    }

    private void ApplyLiveMarketFilter()
    {
        if (string.IsNullOrWhiteSpace(_liveMarketSearchText))
        {
            // No filter, show all companies
            ReplaceCollection(FilteredLiveCompanies, LiveCompanies);
        }
        else
        {
            // Filter by symbol, name, or sector
            var filtered = LiveCompanies.Where(c =>
                c.Symbol.Contains(_liveMarketSearchText, StringComparison.OrdinalIgnoreCase) ||
                c.SecurityName.Contains(_liveMarketSearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Sector.Contains(_liveMarketSearchText, StringComparison.OrdinalIgnoreCase)
            ).ToList();
            
            ReplaceCollection(FilteredLiveCompanies, filtered);
        }
    }
}

public sealed class MarketSummaryRowViewModel
{
    public MarketSummaryRowViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}

public sealed class IndexSnapshotViewModel
{
    public IndexSnapshotViewModel(string name, string symbol, string value, string change, decimal changePercent, decimal currentValue = 0m)
    {
        Name = name;
        Symbol = symbol;
        Value = value;
        Change = change;
        ChangePercent = $"{changePercent:+0.##;-0.##;0}%";
        CurrentValueNumeric = currentValue;
        ChangePercentNumeric = changePercent;
        IsPositive = changePercent >= 0;
        IsNegative = changePercent < 0;
        
        // Arrow and color for UI
        Arrow = changePercent >= 0 ? "▲" : "▼";
        ArrowColor = changePercent >= 0 ? "#22C55E" : "#EF4444";
        ChangeColor = changePercent >= 0 ? "#22C55E" : "#EF4444";
    }

    public string Name { get; }

    public string Symbol { get; }

    public string Value { get; }

    public string Change { get; }

    public string ChangePercent { get; }

    public decimal CurrentValueNumeric { get; }

    public decimal ChangePercentNumeric { get; }

    public bool IsPositive { get; }

    public bool IsNegative { get; }
    
    public string Arrow { get; }
    
    public string ArrowColor { get; }
    
    public string ChangeColor { get; }
}

public sealed class MarketMoverRowViewModel
{
    public MarketMoverRowViewModel(string symbol, string name, string ltp, string change, string metric, decimal ltpValue, decimal changeValue, decimal metricValue, string badge, string? logoPath)
    {
        Symbol = symbol;
        Name = name;
        LastTradedPrice = ltp;
        Change = change;
        Metric = metric;
        LastTradedPriceValue = ltpValue;
        ChangeValue = changeValue;
        MetricValue = metricValue;
        Badge = badge;
        LogoUrl = string.IsNullOrWhiteSpace(logoPath) 
            ? string.Empty 
            : $"https://cdn.arthakendra.com/{logoPath}";
        
        // Determine color based on badge
        if (badge == "▲")
        {
            ChangeColor = "#22C55E"; // Green for gainers
            MetricColor = "#22C55E";
        }
        else if (badge == "▼")
        {
            ChangeColor = "#EF4444"; // Red for losers
            MetricColor = "#EF4444";
        }
        else if (badge == "Rs")
        {
            ChangeColor = "#3B82F6"; // Blue for turnover
            MetricColor = "#3B82F6";
        }
        else if (badge == "Qty")
        {
            ChangeColor = "#8B5CF6"; // Purple for volume
            MetricColor = "#8B5CF6";
        }
        else if (badge == "Tx")
        {
            ChangeColor = "#F59E0B"; // Amber for trades
            MetricColor = "#F59E0B";
        }
        else
        {
            ChangeColor = "#22C55E"; // Default green
            MetricColor = "#22C55E";
        }
    }

    public string Symbol { get; }

    public string Name { get; }

    public string LastTradedPrice { get; }

    public string Change { get; }

    public string Metric { get; }

    public decimal LastTradedPriceValue { get; }

    public decimal ChangeValue { get; }

    public decimal MetricValue { get; }

    public string Badge { get; }

    public string LogoUrl { get; }

    public string ChangeColor { get; }

    public string MetricColor { get; }
}

public sealed class LiveCompanyRowViewModel
{
    public LiveCompanyRowViewModel(string symbol, string securityName, string sector, string open, string high, string low, string ltp, string prevClose, string changePercent, string turnover, string quantity, string transactions, decimal openValue, decimal highValue, decimal lowValue, decimal ltpValue, decimal previousCloseValue, decimal changePercentValue, decimal quantityValue, decimal turnoverValue, int transactionsValue, string? logoPath)
    {
        Symbol = symbol;
        SecurityName = securityName;
        Sector = sector;
        OpenPrice = open;
        HighPrice = high;
        LowPrice = low;
        LastTradedPrice = ltp;
        PreviousClose = prevClose;
        ChangePercent = changePercent;
        Turnover = turnover;
        Quantity = quantity;
        Transactions = transactions;
        OpenPriceValue = openValue;
        HighPriceValue = highValue;
        LowPriceValue = lowValue;
        LastTradedPriceValue = ltpValue;
        PreviousCloseValue = previousCloseValue;
        ChangePercentValue = changePercentValue;
        QuantityValue = quantityValue;
        TurnoverValue = turnoverValue;
        TransactionsValue = transactionsValue;

        RowBackground = changePercentValue > 0m
            ? "#1322C55E"
            : changePercentValue < 0m
                ? "#13EF4444"
                : "Transparent";

        OpenPriceColor = GetSignedColor(openValue - previousCloseValue);
        HighPriceColor = GetSignedColor(highValue - previousCloseValue);
        LowPriceColor = GetSignedColor(lowValue - previousCloseValue);
        LastTradedPriceColor = GetSignedColor(ltpValue - previousCloseValue);
        PreviousCloseColor = "#9CA3AF";
        ChangePercentColor = GetSignedColor(changePercentValue);
        QuantityColor = GetSignedColor(changePercentValue);
        TurnoverColor = GetSignedColor(changePercentValue);
        TransactionsColor = GetSignedColor(changePercentValue);

        LogoUrl = string.IsNullOrWhiteSpace(logoPath) 
            ? string.Empty 
            : $"https://cdn.arthakendra.com/{logoPath}";
    }

    private static string GetSignedColor(decimal value)
    {
        if (value > 0m)
        {
            return "#22C55E";
        }

        if (value < 0m)
        {
            return "#EF4444";
        }

        return "#A3A3A3";
    }

    public string Symbol { get; }

    public string SecurityName { get; }

    public string Sector { get; }

    public string OpenPrice { get; }

    public string HighPrice { get; }

    public string LowPrice { get; }

    public string LastTradedPrice { get; }

    public string PreviousClose { get; }

    public string ChangePercent { get; }

    public string Turnover { get; }

    public string Quantity { get; }

    public string Transactions { get; }

    public decimal OpenPriceValue { get; }

    public decimal HighPriceValue { get; }

    public decimal LowPriceValue { get; }

    public decimal LastTradedPriceValue { get; }

    public decimal PreviousCloseValue { get; }

    public decimal ChangePercentValue { get; }

    public decimal QuantityValue { get; }

    public decimal TurnoverValue { get; }

    public int TransactionsValue { get; }

    public string RowBackground { get; }

    public string OpenPriceColor { get; }

    public string HighPriceColor { get; }

    public string LowPriceColor { get; }

    public string LastTradedPriceColor { get; }

    public string PreviousCloseColor { get; }

    public string ChangePercentColor { get; }

    public string QuantityColor { get; }

    public string TurnoverColor { get; }

    public string TransactionsColor { get; }

    public string LogoUrl { get; }
}
