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
    private readonly AppServices _services;

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

    public BullionViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    public ICommand RefreshCommand { get; }

    /// <summary>Historical rows, newest first.</summary>
    public ObservableCollection<BullionRowViewModel> Rows { get; } = new();

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
            var response = await _services.Bullion.FetchAsync(cancellationToken: cts.Token);

            if (response is null || response.Entries.Count == 0)
            {
                ErrorMessage = "No bullion data returned by the server.";
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
                return;
            }

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

            ReplaceCollection(Rows, newRows);
            HasData = true;
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
