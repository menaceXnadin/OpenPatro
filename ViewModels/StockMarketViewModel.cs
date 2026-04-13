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

    public StockMarketViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
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

    public async Task InitializeAsync()
    {
        if (MajorIndices.Count > 0 || TopGainers.Count > 0 || TopLosers.Count > 0 || LiveCompanies.Count > 0)
        {
            return;
        }

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var response = await _services.ShareHubNepse.FetchHomePageDataAsync();
            if (response is null)
            {
                ErrorMessage = "No stock market data returned by the server.";
                return;
            }

            MapResponse(response);
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

        TotalTurnover = FormatLargeNumber(summary.FirstOrDefault(s => s.Name.Contains("Turnover", StringComparison.OrdinalIgnoreCase))?.Value ?? 0);
        TotalTransactions = FormatLargeNumber(summary.FirstOrDefault(s => s.Name.Contains("Transactions", StringComparison.OrdinalIgnoreCase))?.Value ?? 0);
        TotalTradedShares = FormatLargeNumber(summary.FirstOrDefault(s => s.Name.Contains("Traded Shares", StringComparison.OrdinalIgnoreCase))?.Value ?? 0);
        ScriptsTraded = FormatCount(summary.FirstOrDefault(s => s.Name.Contains("Scrips Traded", StringComparison.OrdinalIgnoreCase))?.Value ?? 0);
        MarketCap = FormatLargeNumber(summary.FirstOrDefault(s => s.Name.Contains("Market Capitalization", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Float", StringComparison.OrdinalIgnoreCase))?.Value ?? 0);
        FloatMarketCap = FormatLargeNumber(summary.FirstOrDefault(s => s.Name.Contains("Float Market Capitalization", StringComparison.OrdinalIgnoreCase))?.Value ?? 0);

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

        ReplaceCollection(MajorIndices, (response.Indices ?? new List<MarketIndexInfo>())
            .Select(i => new IndexSnapshotViewModel(
                NormalizeWhitespace(i.Name),
                i.Symbol,
                i.CurrentValue.ToString("N2", CultureInfo.InvariantCulture),
                $"{i.Change:+0.##;-0.##;0}",
                i.ChangePercent)));

        ReplaceCollection(SectorIndices, (response.SubIndices ?? new List<MarketIndexInfo>())
            .Select(i => new IndexSnapshotViewModel(
                NormalizeWhitespace(i.Name),
                i.Symbol,
                i.CurrentValue.ToString("N2", CultureInfo.InvariantCulture),
                $"{i.Change:+0.##;-0.##;0}",
                i.ChangePercent)));

        ReplaceCollection(TopGainers, (response.TopGainers ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"+{m.Change:0.##}",
                $"{m.ChangePercent:+0.##;-0.##;0}%",
                "▲",
                m.CompanyLogo)));

        ReplaceCollection(TopLosers, (response.TopLosers ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:0.##}",
                $"{m.ChangePercent:+0.##;-0.##;0}%",
                "▼",
                m.CompanyLogo)));

        ReplaceCollection(TopTurnover, (response.TopTurnover ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:+0.##;-0.##;0}",
                FormatLargeNumber(m.Turnover),
                "Rs",
                m.CompanyLogo)));

        ReplaceCollection(TopTradedShares, (response.TopTradedShares ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:+0.##;-0.##;0}",
                FormatLargeNumber(m.SharesTraded),
                "Qty",
                m.CompanyLogo)));

        ReplaceCollection(TopTransactions, (response.TopTransactions ?? new List<MarketMoverInfo>())
            .Select(m => new MarketMoverRowViewModel(
                m.Symbol,
                NormalizeWhitespace(m.Name),
                m.LastTradedPrice.ToString("N2", CultureInfo.InvariantCulture),
                $"{m.Change:+0.##;-0.##;0}",
                FormatCount(m.Transactions),
                "Tx",
                m.CompanyLogo)));

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
                c.CompanyLogo)));

        DemandCount = GetJsonArrayCount(response.Demand).ToString(CultureInfo.InvariantCulture);
        SupplyCount = GetJsonArrayCount(response.Supply).ToString(CultureInfo.InvariantCulture);
        LiveCompanyCount = LiveCompanies.Count.ToString("N0", CultureInfo.InvariantCulture);
        DemandSummary = $"Demand entries: {DemandCount}";
        SupplySummary = $"Supply entries: {SupplyCount}";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
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

    private static string FormatLargeNumber(decimal value)
    {
        if (value >= 1_000_000_000_000) // Trillion
        {
            return $"{value / 1_000_000_000_000:0.##} T";
        }
        if (value >= 1_000_000_000) // Billion
        {
            return $"{value / 1_000_000_000:0.##} B";
        }
        if (value >= 1_000_000) // Million
        {
            return $"{value / 1_000_000:0.##} M";
        }
        if (value >= 1_000) // Thousand
        {
            return $"{value / 1_000:0.##} K";
        }
        return value.ToString("N0", CultureInfo.InvariantCulture);
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
    public IndexSnapshotViewModel(string name, string symbol, string value, string change, decimal changePercent)
    {
        Name = name;
        Symbol = symbol;
        Value = value;
        Change = change;
        ChangePercent = $"{changePercent:+0.##;-0.##;0}%";
        IsPositive = changePercent >= 0;
        IsNegative = changePercent < 0;
    }

    public string Name { get; }

    public string Symbol { get; }

    public string Value { get; }

    public string Change { get; }

    public string ChangePercent { get; }

    public bool IsPositive { get; }

    public bool IsNegative { get; }
}

public sealed class MarketMoverRowViewModel
{
    public MarketMoverRowViewModel(string symbol, string name, string ltp, string change, string metric, string badge, string? logoPath)
    {
        Symbol = symbol;
        Name = name;
        LastTradedPrice = ltp;
        Change = change;
        Metric = metric;
        Badge = badge;
        LogoUrl = string.IsNullOrWhiteSpace(logoPath) 
            ? string.Empty 
            : $"https://cdn.arthakendra.com/{logoPath}";
    }

    public string Symbol { get; }

    public string Name { get; }

    public string LastTradedPrice { get; }

    public string Change { get; }

    public string Metric { get; }

    public string Badge { get; }

    public string LogoUrl { get; }
}

public sealed class LiveCompanyRowViewModel
{
    public LiveCompanyRowViewModel(string symbol, string securityName, string sector, string open, string high, string low, string ltp, string prevClose, string changePercent, string turnover, string quantity, string transactions, string? logoPath)
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
        LogoUrl = string.IsNullOrWhiteSpace(logoPath) 
            ? string.Empty 
            : $"https://cdn.arthakendra.com/{logoPath}";
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

    public string LogoUrl { get; }
}
