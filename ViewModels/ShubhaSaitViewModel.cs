using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;

namespace OpenPatro.ViewModels;

/// <summary>
/// ViewModel for the Shubha Sait (auspicious dates) feature.
/// Fetches encrypted data from the NepaliPatro API, decrypts it, and exposes
/// year → category → month → auspicious days navigation.
/// </summary>
public sealed class ShubhaSaitViewModel : BindableBase
{
    private readonly AppServices _services;

    // Parsed data: Year → CategoryFullKey → Month → list of day strings
    private Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>? _cachedData;

    private string? _selectedYear;
    private string? _selectedCategoryKey;
    private string? _selectedMonth;
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    public ShubhaSaitViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    /// <summary>BS month numbers to Nepali month names.</summary>
    public static ReadOnlyDictionary<string, string> BsMonthNames { get; } = new(new Dictionary<string, string>
    {
        ["01"] = "बैशाख",
        ["02"] = "जेठ",
        ["03"] = "असार",
        ["04"] = "श्रावण",
        ["05"] = "भदौ",
        ["06"] = "असोज",
        ["07"] = "कार्तिक",
        ["08"] = "मंसिर",
        ["09"] = "पौष",
        ["10"] = "माघ",
        ["11"] = "फाल्गुण",
        ["12"] = "चैत"
    });

    public ICommand RefreshCommand { get; }

    /// <summary>Available BS year strings from the data.</summary>
    public ObservableCollection<string> AvailableYears { get; } = new();

    /// <summary>Available category display info for the selected year.</summary>
    public ObservableCollection<SaitCategoryInfo> AvailableCategories { get; } = new();

    /// <summary>Available months for the selected year + category.</summary>
    public ObservableCollection<SaitMonthInfo> AvailableMonths { get; } = new();

    /// <summary>Auspicious day numbers for the current selection.</summary>
    public ObservableCollection<string> AuspiciousDays { get; } = new();

    public string? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (SetProperty(ref _selectedYear, value))
            {
                PopulateCategories();
            }
        }
    }

    public string? SelectedCategoryKey
    {
        get => _selectedCategoryKey;
        set
        {
            if (SetProperty(ref _selectedCategoryKey, value))
            {
                PopulateMonths();
            }
        }
    }

    public string? SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (SetProperty(ref _selectedMonth, value))
            {
                PopulateDays();
            }
        }
    }

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

    /// <summary>
    /// Called once on page load. Fetches, decrypts, parses, and caches the data.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_cachedData is not null)
        {
            PopulateYears();
            return;
        }

        await FetchAndCacheAsync();
    }

    /// <summary>
    /// Force-refresh: clears session cache and re-fetches.
    /// </summary>
    public async Task RefreshAsync()
    {
        _cachedData = null;
        await FetchAndCacheAsync();
    }

    private async Task FetchAndCacheAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var json = await _services.NepaliPatroApi.FetchShubhaSaitRawJsonAsync();
            _cachedData = ParseShubhaSaitJson(json);
            PopulateYears();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Shubha Sait: {ex.Message}";
            Debug.WriteLine($"ShubhaSaitViewModel fetch error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Parses the decrypted JSON into a structured dictionary.
    /// Structure: Year → CategoryFullKey → Month → List of day strings (the keys of the innermost objects).
    /// </summary>
    private static Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> ParseShubhaSaitJson(string json)
    {
        var result = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();

        using var doc = JsonDocument.Parse(json);
        foreach (var yearProp in doc.RootElement.EnumerateObject())
        {
            var yearKey = yearProp.Name;
            var categories = new Dictionary<string, Dictionary<string, List<string>>>();

            foreach (var categoryProp in yearProp.Value.EnumerateObject())
            {
                var categoryKey = categoryProp.Name;
                var months = new Dictionary<string, List<string>>();

                foreach (var monthProp in categoryProp.Value.EnumerateObject())
                {
                    var monthKey = monthProp.Name;
                    var days = new List<string>();

                    foreach (var dayProp in monthProp.Value.EnumerateObject())
                    {
                        days.Add(dayProp.Name);
                    }

                    days.Sort((a, b) => int.Parse(a).CompareTo(int.Parse(b)));
                    months[monthKey] = days;
                }

                categories[categoryKey] = months;
            }

            result[yearKey] = categories;
        }

        return result;
    }

    private void PopulateYears()
    {
        // Reset backing fields so SetProperty always detects a change
        // and triggers the downstream cascade (PopulateCategories etc.).
        // Without this, a prior background-thread run can leave stale
        // values that cause SetProperty to return false.
        _selectedYear = null;
        _selectedCategoryKey = null;
        _selectedMonth = null;

        AvailableYears.Clear();
        AvailableCategories.Clear();
        AvailableMonths.Clear();
        AuspiciousDays.Clear();

        if (_cachedData is null)
        {
            return;
        }

        foreach (var year in _cachedData.Keys.OrderByDescending(y => y))
        {
            AvailableYears.Add(year);
        }

        // Auto-select the first (latest) year
        SelectedYear = AvailableYears.FirstOrDefault();
    }

    private void PopulateCategories()
    {
        _selectedCategoryKey = null;
        _selectedMonth = null;

        AvailableCategories.Clear();
        AvailableMonths.Clear();
        AuspiciousDays.Clear();

        if (_cachedData is null || SelectedYear is null || !_cachedData.ContainsKey(SelectedYear))
        {
            return;
        }

        var yearData = _cachedData[SelectedYear];
        foreach (var categoryFullKey in yearData.Keys)
        {
            AvailableCategories.Add(new SaitCategoryInfo(categoryFullKey));
        }

        // Auto-select the first category
        SelectedCategoryKey = AvailableCategories.FirstOrDefault()?.FullKey;
    }

    private void PopulateMonths()
    {
        _selectedMonth = null;

        AvailableMonths.Clear();
        AuspiciousDays.Clear();

        if (_cachedData is null || SelectedYear is null || SelectedCategoryKey is null)
        {
            return;
        }

        if (!_cachedData.TryGetValue(SelectedYear, out var yearData) ||
            !yearData.TryGetValue(SelectedCategoryKey, out var monthData))
        {
            return;
        }

        foreach (var monthKey in monthData.Keys.OrderBy(m => int.Parse(m)))
        {
            var displayName = BsMonthNames.TryGetValue(monthKey, out var name) ? name : $"Month {monthKey}";
            AvailableMonths.Add(new SaitMonthInfo(monthKey, displayName));
        }

        // Auto-select the first month
        SelectedMonth = AvailableMonths.FirstOrDefault()?.MonthKey;
    }

    private void PopulateDays()
    {
        AuspiciousDays.Clear();

        if (_cachedData is null || SelectedYear is null || SelectedCategoryKey is null || SelectedMonth is null)
        {
            return;
        }

        if (!_cachedData.TryGetValue(SelectedYear, out var yearData) ||
            !yearData.TryGetValue(SelectedCategoryKey, out var monthData) ||
            !monthData.TryGetValue(SelectedMonth, out var days))
        {
            return;
        }

        foreach (var day in days)
        {
            AuspiciousDays.Add(day);
        }
    }

    /// <summary>
    /// Gets the auspicious days for a specific year, category, and month programmatically.
    /// </summary>
    public List<string> GetAuspiciousDays(string year, string categoryFullKey, string month)
    {
        if (_cachedData is null)
        {
            return new List<string>();
        }

        if (!_cachedData.TryGetValue(year, out var yearData) ||
            !yearData.TryGetValue(categoryFullKey, out var monthData) ||
            !monthData.TryGetValue(month, out var days))
        {
            return new List<string>();
        }

        return new List<string>(days);
    }
}

/// <summary>
/// Represents a Shubha Sait category for UI binding.
/// The full key is like "Hom~होम गर्ने साइत" — the part after ~ is the Nepali display name.
/// </summary>
public sealed class SaitCategoryInfo
{
    public SaitCategoryInfo(string fullKey)
    {
        FullKey = fullKey;
        var parts = fullKey.Split('~', 2);
        NepaliName = parts.Length > 1 ? parts[1] : fullKey;
        EnglishSlug = parts.Length > 1 ? parts[0] : fullKey;
    }

    public string FullKey { get; }
    public string NepaliName { get; }
    public string EnglishSlug { get; }
}

/// <summary>
/// Represents a BS month for the Shubha Sait month picker.
/// </summary>
public sealed class SaitMonthInfo
{
    public SaitMonthInfo(string monthKey, string displayName)
    {
        MonthKey = monthKey;
        DisplayName = displayName;
    }

    public string MonthKey { get; }
    public string DisplayName { get; }
}
