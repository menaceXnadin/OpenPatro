using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Models;

namespace OpenPatro.ViewModels;

/// <summary>
/// ViewModel for the Rashifal (horoscope) feature.
/// Fetches from the NepaliPatro API, caches for the session, supports refresh.
/// </summary>
public sealed class RashifalViewModel : BindableBase
{
    private readonly AppServices _services;
    private RashifalResponse? _cachedResponse;

    private string _selectedType = "D";
    private string _title = string.Empty;
    private string _validForDate = string.Empty;
    private string _selectedZodiacKey = "aries";
    private string _predictionText = string.Empty;
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    public RashifalViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    /// <summary>Ordered zodiac entries for picker binding.</summary>
    public static ReadOnlyCollection<ZodiacInfo> ZodiacSigns { get; } = new(new List<ZodiacInfo>
    {
        new("aries",       "मेष",     "Aries"),
        new("taurus",      "वृष",     "Taurus"),
        new("gemini",      "मिथुन",   "Gemini"),
        new("cancer",      "कर्कट",   "Cancer"),
        new("leo",         "सिंह",    "Leo"),
        new("virgo",       "कन्या",   "Virgo"),
        new("libra",       "तुला",    "Libra"),
        new("scorpio",     "वृश्चिक", "Scorpio"),
        new("sagittarius", "धनु",     "Sagittarius"),
        new("capricorn",   "मकर",     "Capricorn"),
        new("aquarius",    "कुम्भ",   "Aquarius"),
        new("pisces",      "मीन",     "Pisces")
    });

    /// <summary>Available rashifal types for the picker.</summary>
    public static ReadOnlyCollection<RashifalTypeInfo> RashifalTypes { get; } = new(new List<RashifalTypeInfo>
    {
        new("D", "दैनिक", "Daily"),
        new("W", "साप्ताहिक", "Weekly"),
        new("M", "मासिक", "Monthly"),
        new("Y", "वार्षिक", "Yearly")
    });

    public ICommand RefreshCommand { get; }

    public ReadOnlyCollection<ZodiacInfo> AvailableZodiacSigns => ZodiacSigns;
    public ReadOnlyCollection<RashifalTypeInfo> AvailableRashifalTypes => RashifalTypes;

    public string SelectedType
    {
        get => _selectedType;
        set
        {
            if (SetProperty(ref _selectedType, value))
            {
                UpdateFromCache();
            }
        }
    }

    public string SelectedZodiacKey
    {
        get => _selectedZodiacKey;
        set
        {
            if (SetProperty(ref _selectedZodiacKey, value))
            {
                UpdatePrediction();
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string ValidForDate
    {
        get => _validForDate;
        private set => SetProperty(ref _validForDate, value);
    }

    public string PredictionText
    {
        get => _predictionText;
        private set => SetProperty(ref _predictionText, value);
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
    /// Called once on page load. Fetches and caches data, then updates display.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_cachedResponse is not null)
        {
            UpdateFromCache();
            return;
        }

        await FetchAndCacheAsync();
    }

    /// <summary>
    /// Force-refresh: clears session cache and re-fetches from the network.
    /// </summary>
    private async Task RefreshAsync()
    {
        _cachedResponse = null;
        await FetchAndCacheAsync();
    }

    private async Task FetchAndCacheAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            _cachedResponse = await _services.NepaliPatroApi.FetchRashifalAsync();
            UpdateFromCache();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Rashifal: {ex.Message}";
            Debug.WriteLine($"RashifalViewModel fetch error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateFromCache()
    {
        if (_cachedResponse is null)
        {
            return;
        }

        var entry = _cachedResponse.Np.FirstOrDefault(e => e.Type == SelectedType);
        if (entry is null)
        {
            Title = string.Empty;
            ValidForDate = string.Empty;
            PredictionText = "No data available for this type.";
            return;
        }

        Title = entry.Title;
        ValidForDate = entry.ToDate;
        UpdatePrediction();
    }

    private void UpdatePrediction()
    {
        if (_cachedResponse is null)
        {
            return;
        }

        var entry = _cachedResponse.Np.FirstOrDefault(e => e.Type == SelectedType);
        PredictionText = entry?.GetPrediction(SelectedZodiacKey) ?? string.Empty;
    }
}

/// <summary>
/// Represents a zodiac sign for UI binding.
/// </summary>
public sealed class ZodiacInfo
{
    public ZodiacInfo(string key, string nepaliName, string englishName)
    {
        Key = key;
        NepaliName = nepaliName;
        EnglishName = englishName;
        DisplayName = $"{nepaliName} ({englishName})";
    }

    public string Key { get; }
    public string NepaliName { get; }
    public string EnglishName { get; }
    public string DisplayName { get; }
}

/// <summary>
/// Represents a rashifal type (Daily/Weekly/Monthly/Yearly) for UI binding.
/// </summary>
public sealed class RashifalTypeInfo
{
    public RashifalTypeInfo(string type, string nepaliName, string englishName)
    {
        Type = type;
        NepaliName = nepaliName;
        EnglishName = englishName;
        DisplayName = $"{nepaliName} ({englishName})";
    }

    public string Type { get; }
    public string NepaliName { get; }
    public string EnglishName { get; }
    public string DisplayName { get; }
}
