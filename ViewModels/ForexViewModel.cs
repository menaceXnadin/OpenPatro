using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Models;

namespace OpenPatro.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// Row view model for the today's rates table
// ─────────────────────────────────────────────────────────────────────────────
public sealed class ForexRateRowViewModel
{
    private const string FlagCdnBaseUrl = "https://flagcdn.com/w40";

    public ForexRateRowViewModel(ForexRateEntry entry)
    {
        Code = entry.Code;
        Currency = entry.Currency;
        Unit = entry.Unit;
        Buying = entry.Buying;
        Selling = entry.Selling;
        Type = entry.Type;

        UnitLabel = entry.Unit == 1
            ? entry.Code
            : $"{entry.Unit} {entry.Code}";

        BuyingText = entry.Buying.ToString("N2", CultureInfo.InvariantCulture);
        SellingText = entry.Selling.ToString("N2", CultureInfo.InvariantCulture);

        // Flag emoji by currency code
        Flag = GetFlag(entry.Code);
        FlagCountryCode = GetFlagCountryCode(entry.Code);
    }

    internal static string GetFlag(string code) => code switch
    {
        "USD" => "🇺🇸", "EUR" => "🇪🇺", "GBP" => "🇬🇧", "INR" => "🇮🇳",
        "CHF" => "🇨🇭", "AUD" => "🇦🇺", "CAD" => "🇨🇦", "SGD" => "🇸🇬",
        "JPY" => "🇯🇵", "CNY" => "🇨🇳", "SAR" => "🇸🇦", "QAR" => "🇶🇦",
        "THB" => "🇹🇭", "AED" => "🇦🇪", "MYR" => "🇲🇾", "KRW" => "🇰🇷",
        "SEK" => "🇸🇪", "DKK" => "🇩🇰", "HKD" => "🇭🇰", "KWD" => "🇰🇼",
        "BHD" => "🇧🇭", "OMR" => "🇴🇲", "NPR" => "🇳🇵",
        _ => "🏳️"
    };

    public string Code { get; }
    public string Currency { get; }
    public int Unit { get; }
    public decimal Buying { get; }
    public decimal Selling { get; }
    public string Type { get; }
    public string UnitLabel { get; }
    public string BuyingText { get; }
    public string SellingText { get; }
    public string Flag { get; }
    public string? FlagCountryCode { get; }
    public string? FlagCdnUrl => FlagCountryCode is null ? null : $"{FlagCdnBaseUrl}/{FlagCountryCode}.png";

    internal static string? GetFlagCountryCode(string code) => code switch
    {
        "USD" => "us",
        "EUR" => "eu",
        "GBP" => "gb",
        "INR" => "in",
        "CHF" => "ch",
        "AUD" => "au",
        "CAD" => "ca",
        "SGD" => "sg",
        "JPY" => "jp",
        "CNY" => "cn",
        "SAR" => "sa",
        "QAR" => "qa",
        "THB" => "th",
        "AED" => "ae",
        "MYR" => "my",
        "KRW" => "kr",
        "SEK" => "se",
        "DKK" => "dk",
        "HKD" => "hk",
        "KWD" => "kw",
        "BHD" => "bh",
        "OMR" => "om",
        "NPR" => "np",
        _ => null
    };
}

public sealed class ForexCurrencyOption
{
    public ForexCurrencyOption(string code, string currency, int unit, decimal buying)
    {
        Code = code;
        Currency = currency;
        Unit = unit;
        Buying = buying;
        Flag = ForexRateRowViewModel.GetFlag(code);
    }

    public string Code { get; }
    public string Currency { get; }
    public int Unit { get; }
    public decimal Buying { get; }
    public string Flag { get; }
    public string DisplayLabel => $"{Code} · {Currency}";
    public string? FlagCountryCode => ForexRateRowViewModel.GetFlagCountryCode(Code);
    public string? FlagCdnUrl => FlagCountryCode is null ? null : $"https://flagcdn.com/w40/{FlagCountryCode}.png";
    public decimal RatePerUnitNpr => Code == "NPR" ? 1m : (Unit <= 0 ? 0m : Buying / Unit);
}

// ─────────────────────────────────────────────────────────────────────────────
// A single point on the history chart
// ─────────────────────────────────────────────────────────────────────────────
public sealed class ForexChartPoint
{
    public ForexChartPoint(string date, decimal buying, decimal selling)
    {
        Date = date;
        Buying = buying;
        Selling = selling;
    }

    public string Date { get; }
    public decimal Buying { get; }
    public decimal Selling { get; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Main ViewModel
// ─────────────────────────────────────────────────────────────────────────────
public sealed class ForexViewModel : BindableBase
{
    private readonly AppServices _services;
    private const string DefaultFromCurrencyCode = "USD";
    private const string DefaultToCurrencyCode = "NPR";

    // Today's data
    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _ratesDate = "--";
    private bool _hasData;

    // Selected currency + history
    private ForexRateRowViewModel? _selectedCurrency;
    private bool _isHistoryBusy;
    private string _historyError = string.Empty;
    private bool _hasHistory;
    private string _historyMin = "--";
    private string _historyMax = "--";
    private string _historyAvg = "--";
    private string _historyChange = "--";
    private bool _historyChangeUp;
    private bool _historyChangeDown;
    private string _selectedCurrencyLabel = string.Empty;

    // Converter state
    private ForexCurrencyOption? _converterFromCurrency;
    private ForexCurrencyOption? _converterToCurrency;
    private string _converterFromAmount = "1";
    private string _converterToAmount = string.Empty;
    private string _converterRateSummary = string.Empty;
    private bool _isUpdatingConverterValues;
    private ConverterInputSide _lastEditedSide = ConverterInputSide.From;

    // Cancellation for in-flight history requests
    private CancellationTokenSource? _historyCts;

    public ForexViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SelectCurrencyCommand = new AsyncRelayCommand<ForexRateRowViewModel>(SelectCurrencyAsync);
        CloseHistoryCommand = new RelayCommand(CloseHistory);
        SwapCurrenciesCommand = new RelayCommand(SwapCurrencies);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SelectCurrencyCommand { get; }
    public ICommand CloseHistoryCommand { get; }
    public ICommand SwapCurrenciesCommand { get; }

    /// <summary>All currency rows for today's table.</summary>
    public ObservableCollection<ForexRateRowViewModel> Rates { get; } = new();

    /// <summary>Chart data points for the selected currency (oldest → newest).</summary>
    public ObservableCollection<ForexChartPoint> ChartPoints { get; } = new();

    /// <summary>Supported currencies for converter.</summary>
    public ObservableCollection<ForexCurrencyOption> SupportedCurrencies { get; } = new();

    // ── Today's data ──

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string RatesDate
    {
        get => _ratesDate;
        private set => SetProperty(ref _ratesDate, value);
    }

    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    // ── Selected currency / history panel ──

    public ForexRateRowViewModel? SelectedCurrency
    {
        get => _selectedCurrency;
        private set
        {
            if (SetProperty(ref _selectedCurrency, value))
                RaisePropertyChanged(nameof(IsHistoryPanelVisible));
        }
    }

    public bool IsHistoryPanelVisible => SelectedCurrency is not null;

    public bool IsHistoryBusy
    {
        get => _isHistoryBusy;
        private set => SetProperty(ref _isHistoryBusy, value);
    }

    public string HistoryError
    {
        get => _historyError;
        private set => SetProperty(ref _historyError, value);
    }

    public bool HasHistory
    {
        get => _hasHistory;
        private set => SetProperty(ref _hasHistory, value);
    }

    public string HistoryMin
    {
        get => _historyMin;
        private set => SetProperty(ref _historyMin, value);
    }

    public string HistoryMax
    {
        get => _historyMax;
        private set => SetProperty(ref _historyMax, value);
    }

    public string HistoryAvg
    {
        get => _historyAvg;
        private set => SetProperty(ref _historyAvg, value);
    }

    public string HistoryChange
    {
        get => _historyChange;
        private set => SetProperty(ref _historyChange, value);
    }

    public bool HistoryChangeUp
    {
        get => _historyChangeUp;
        private set => SetProperty(ref _historyChangeUp, value);
    }

    public bool HistoryChangeDown
    {
        get => _historyChangeDown;
        private set => SetProperty(ref _historyChangeDown, value);
    }

    public string SelectedCurrencyLabel
    {
        get => _selectedCurrencyLabel;
        private set => SetProperty(ref _selectedCurrencyLabel, value);
    }

    public ForexCurrencyOption? ConverterFromCurrency
    {
        get => _converterFromCurrency;
        set
        {
            if (SetProperty(ref _converterFromCurrency, value))
                UpdateConversionFromEditedSide();
        }
    }

    public ForexCurrencyOption? ConverterToCurrency
    {
        get => _converterToCurrency;
        set
        {
            if (SetProperty(ref _converterToCurrency, value))
                UpdateConversionFromEditedSide();
        }
    }

    public string ConverterFromAmount
    {
        get => _converterFromAmount;
        set
        {
            if (SetProperty(ref _converterFromAmount, value) && !_isUpdatingConverterValues)
            {
                _lastEditedSide = ConverterInputSide.From;
                UpdateConversionFromEditedSide();
            }
        }
    }

    public string ConverterToAmount
    {
        get => _converterToAmount;
        set
        {
            if (SetProperty(ref _converterToAmount, value) && !_isUpdatingConverterValues)
            {
                _lastEditedSide = ConverterInputSide.To;
                UpdateConversionFromEditedSide();
            }
        }
    }

    public string ConverterRateSummary
    {
        get => _converterRateSummary;
        private set => SetProperty(ref _converterRateSummary, value);
    }

    // ── Init / Refresh ──

    public async Task InitializeAsync()
    {
        if (HasData || IsBusy) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var response = await _services.Forex.FetchTodayAsync(cts.Token);

            if (response?.Data is null || response.Data.Count == 0)
            {
                ErrorMessage = "No forex data returned by the server.";
                return;
            }

            RatesDate = TryFormatDate(response.Date);

            var newRows = response.Data
                .Select(e => new ForexRateRowViewModel(e))
                .ToList();

            ReplaceCollection(Rates, newRows);
            RebuildConverterCurrencies(newRows);
            HasData = true;

            // If a currency was selected, refresh its history too
            if (SelectedCurrency is not null)
            {
                var refreshed = Rates.FirstOrDefault(r =>
                    string.Equals(r.Code, SelectedCurrency.Code, StringComparison.Ordinal));
                if (refreshed is not null)
                    await LoadHistoryAsync(refreshed);
            }
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Forex request timed out. Please try again.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load forex data: {ex.Message}";
            Debug.WriteLine($"ForexViewModel fetch error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Currency selection ──

    private async Task SelectCurrencyAsync(ForexRateRowViewModel? row)
    {
        if (row is null) return;

        // Clicking the same currency again closes the panel
        if (SelectedCurrency is not null &&
            string.Equals(SelectedCurrency.Code, row.Code, StringComparison.Ordinal))
        {
            CloseHistory();
            return;
        }

        SelectedCurrency = row;
        SelectedCurrencyLabel = $"{row.Flag} {row.Currency} ({row.UnitLabel})";
        await LoadHistoryAsync(row);
    }

    private async Task LoadHistoryAsync(ForexRateRowViewModel row)
    {
        // Cancel any in-flight request
        _historyCts?.Cancel();
        _historyCts?.Dispose();
        _historyCts = new CancellationTokenSource();

        IsHistoryBusy = true;
        HistoryError = string.Empty;
        HasHistory = false;
        ChartPoints.Clear();

        try
        {
            var to = DateOnly.FromDateTime(DateTime.Today);
            var from = to.AddDays(-30);

            var history = await _services.Forex.FetchHistoryAsync(
                row.Code, from, to, _historyCts.Token);

            if (history.Count == 0)
            {
                HistoryError = "No history available for this currency.";
                return;
            }

            // Deduplicate by date, keep last entry per date, sort oldest→newest
            var deduped = history
                .GroupBy(h => h.AddedDate)
                .Select(g => g.Last())
                .OrderBy(h => h.AddedDate)
                .ToList();

            foreach (var pt in deduped)
                ChartPoints.Add(new ForexChartPoint(pt.AddedDate, pt.Buying, pt.Selling));

            // Stats
            var buyingValues = deduped.Select(h => h.Buying).ToList();
            HistoryMin = buyingValues.Min().ToString("N2", CultureInfo.InvariantCulture);
            HistoryMax = buyingValues.Max().ToString("N2", CultureInfo.InvariantCulture);
            HistoryAvg = buyingValues.Average().ToString("N2", CultureInfo.InvariantCulture);

            var first = deduped.First().Buying;
            var last = deduped.Last().Buying;
            var diff = last - first;
            HistoryChange = diff >= 0
                ? $"+{diff:N2}"
                : diff.ToString("N2", CultureInfo.InvariantCulture);
            HistoryChangeUp = diff > 0;
            HistoryChangeDown = diff < 0;

            HasHistory = true;
        }
        catch (OperationCanceledException)
        {
            // Silently cancelled — user switched currency
        }
        catch (Exception ex)
        {
            HistoryError = $"Failed to load history: {ex.Message}";
            Debug.WriteLine($"ForexViewModel history error: {ex}");
        }
        finally
        {
            IsHistoryBusy = false;
        }
    }

    private void CloseHistory()
    {
        _historyCts?.Cancel();
        SelectedCurrency = null;
        HasHistory = false;
        HistoryError = string.Empty;
        ChartPoints.Clear();
    }

    private void RebuildConverterCurrencies(IList<ForexRateRowViewModel> rows)
    {
        var currencies = rows
            .Select(r => new ForexCurrencyOption(r.Code, r.Currency, r.Unit, r.Buying))
            .ToList();

        if (!currencies.Any(c => string.Equals(c.Code, "NPR", StringComparison.OrdinalIgnoreCase)))
            currencies.Add(new ForexCurrencyOption("NPR", "Nepalese Rupee", 1, 1m));

        currencies = currencies
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceCollection(SupportedCurrencies, currencies);

        if (SupportedCurrencies.Count == 0)
        {
            ConverterFromCurrency = null;
            ConverterToCurrency = null;
            SetConverterToAmountSilently(string.Empty);
            ConverterRateSummary = string.Empty;
            return;
        }

        var fromCode = ConverterFromCurrency?.Code ?? DefaultFromCurrencyCode;
        var toCode = ConverterToCurrency?.Code ?? DefaultToCurrencyCode;

        ConverterFromCurrency = SupportedCurrencies.FirstOrDefault(c =>
                string.Equals(c.Code, fromCode, StringComparison.OrdinalIgnoreCase))
            ?? SupportedCurrencies.FirstOrDefault(c =>
                string.Equals(c.Code, DefaultFromCurrencyCode, StringComparison.OrdinalIgnoreCase))
            ?? SupportedCurrencies[0];

        ConverterToCurrency = SupportedCurrencies.FirstOrDefault(c =>
                string.Equals(c.Code, toCode, StringComparison.OrdinalIgnoreCase))
            ?? SupportedCurrencies.FirstOrDefault(c =>
                string.Equals(c.Code, DefaultToCurrencyCode, StringComparison.OrdinalIgnoreCase))
            ?? SupportedCurrencies[0];

        if (string.IsNullOrWhiteSpace(ConverterFromAmount))
        {
            SetConverterFromAmountSilently("1");
            _lastEditedSide = ConverterInputSide.From;
        }

        UpdateConversionFromEditedSide();
    }

    private void SwapCurrencies()
    {
        if (ConverterFromCurrency is null || ConverterToCurrency is null)
        {
            return;
        }

        var currentFromCurrency = ConverterFromCurrency;
        var currentToCurrency = ConverterToCurrency;
        var currentFromAmount = ConverterFromAmount;
        var currentToAmount = ConverterToAmount;

        _isUpdatingConverterValues = true;
        ConverterFromCurrency = currentToCurrency;
        ConverterToCurrency = currentFromCurrency;
        SetConverterFromAmountSilently(currentToAmount);
        SetConverterToAmountSilently(currentFromAmount);
        _isUpdatingConverterValues = false;

        _lastEditedSide = ConverterInputSide.From;
        UpdateConversionFromEditedSide();
    }

    private void UpdateConversionFromEditedSide()
    {
        if (ConverterFromCurrency is null || ConverterToCurrency is null)
        {
            SetConverterToAmountSilently(string.Empty);
            ConverterRateSummary = string.Empty;
            return;
        }

        var fromRate = ConverterFromCurrency.RatePerUnitNpr;
        var toRate = ConverterToCurrency.RatePerUnitNpr;
        if (fromRate <= 0 || toRate <= 0)
        {
            ConverterRateSummary = "Rate unavailable";
            return;
        }

        var oneUnitRate = fromRate / toRate;
        ConverterRateSummary = $"1 {ConverterFromCurrency.Code} = {oneUnitRate:N6} {ConverterToCurrency.Code}";

        if (_lastEditedSide == ConverterInputSide.From)
        {
            if (!TryParseAmount(ConverterFromAmount, out var amount) || amount <= 0)
            {
                SetConverterToAmountSilently(string.Empty);
                return;
            }

            var converted = amount * oneUnitRate;
            SetConverterToAmountSilently(FormatAmount(converted));
            return;
        }

        if (!TryParseAmount(ConverterToAmount, out var toAmount) || toAmount <= 0)
        {
            SetConverterFromAmountSilently(string.Empty);
            return;
        }

        var fromAmount = toAmount / oneUnitRate;
        SetConverterFromAmountSilently(FormatAmount(fromAmount));
    }

    private static bool TryParseAmount(string raw, out decimal amount)
    {
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return true;

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out amount);
    }

    private static string FormatAmount(decimal amount)
        => amount.ToString("0.####", CultureInfo.InvariantCulture);

    private void SetConverterFromAmountSilently(string value)
    {
        if (string.Equals(_converterFromAmount, value, StringComparison.Ordinal))
            return;

        _converterFromAmount = value;
        RaisePropertyChanged(nameof(ConverterFromAmount));
    }

    private void SetConverterToAmountSilently(string value)
    {
        if (string.Equals(_converterToAmount, value, StringComparison.Ordinal))
            return;

        _converterToAmount = value;
        RaisePropertyChanged(nameof(ConverterToAmount));
    }

    private enum ConverterInputSide
    {
        From,
        To
    }

    // ── Helpers ──

    private static string TryFormatDate(string raw)
    {
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            return d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        return raw;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IList<T> source)
    {
        int i = 0;
        foreach (var item in source)
        {
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                    target[i] = item;
            }
            else
            {
                target.Add(item);
            }
            i++;
        }
        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AsyncRelayCommand<T> — typed parameter variant needed for SelectCurrencyCommand
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class AsyncRelayCommand<T> : System.Windows.Input.ICommand
{
    private readonly Func<T?, Task> _execute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning;

    public async void Execute(object? parameter)
    {
        if (_isRunning) return;
        _isRunning = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute(parameter is T t ? t : default);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AsyncRelayCommand<T> error: {ex}");
        }
        finally
        {
            _isRunning = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
