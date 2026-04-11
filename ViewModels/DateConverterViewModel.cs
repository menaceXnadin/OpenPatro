using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Models;

namespace OpenPatro.ViewModels;

/// <summary>
/// ViewModel for the Date Converter feature.
/// Supports BS→AD and AD→BS conversion via the NepaliPatro API.
/// </summary>
public sealed class DateConverterViewModel : BindableBase
{
    private readonly AppServices _services;

    private string _inputDate = string.Empty;
    private string _conversionDirection = "BS"; // "BS" = BS→AD, "AD" = AD→BS
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    // Result fields
    private string _resultAdDate = string.Empty;
    private string _resultBsDate = string.Empty;
    private int _resultBsYear;
    private int _resultBsMonth;
    private int _resultBsDay;
    private string _resultTithiLabel = string.Empty;
    private int _resultNsYear;
    private int _resultChandrama;
    private bool _resultIsVerified;
    private bool _hasResult;

    public DateConverterViewModel(AppServices services)
    {
        _services = services;
        ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(InputDate));
        RefreshCommand = new AsyncRelayCommand(ConvertAsync, () => !IsBusy && HasResult);
    }

    public ICommand ConvertCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>
    /// The user's input date string in YYYY-MM-DD format.
    /// </summary>
    public string InputDate
    {
        get => _inputDate;
        set
        {
            if (SetProperty(ref _inputDate, value))
            {
                ((AsyncRelayCommand)ConvertCommand).NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// "BS" for BS→AD conversion, "AD" for AD→BS conversion.
    /// </summary>
    public string ConversionDirection
    {
        get => _conversionDirection;
        set => SetProperty(ref _conversionDirection, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)ConvertCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (SetProperty(ref _hasResult, value))
            {
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
            }
        }
    }

    // --- Result properties ---

    public string ResultAdDate
    {
        get => _resultAdDate;
        private set => SetProperty(ref _resultAdDate, value);
    }

    public string ResultBsDate
    {
        get => _resultBsDate;
        private set => SetProperty(ref _resultBsDate, value);
    }

    public int ResultBsYear
    {
        get => _resultBsYear;
        private set => SetProperty(ref _resultBsYear, value);
    }

    public int ResultBsMonth
    {
        get => _resultBsMonth;
        private set => SetProperty(ref _resultBsMonth, value);
    }

    public int ResultBsDay
    {
        get => _resultBsDay;
        private set => SetProperty(ref _resultBsDay, value);
    }

    public string ResultTithiLabel
    {
        get => _resultTithiLabel;
        private set => SetProperty(ref _resultTithiLabel, value);
    }

    public int ResultNsYear
    {
        get => _resultNsYear;
        private set => SetProperty(ref _resultNsYear, value);
    }

    public int ResultChandrama
    {
        get => _resultChandrama;
        private set => SetProperty(ref _resultChandrama, value);
    }

    public bool ResultIsVerified
    {
        get => _resultIsVerified;
        private set => SetProperty(ref _resultIsVerified, value);
    }

    /// <summary>
    /// Perform the date conversion using the API.
    /// </summary>
    private async Task ConvertAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        HasResult = false;

        try
        {
            var normalizedDate = NormalizeDate(InputDate.Trim());
            var request = new DateConvertRequest
            {
                Date = normalizedDate,
                BasedOn = ConversionDirection
            };

            var response = await _services.NepaliPatroApi.ConvertDateAsync(request);
            if (response is null)
            {
                ErrorMessage = "No response from the server.";
                return;
            }

            ResultAdDate = response.Ad;
            ResultBsDate = response.Bs;
            ResultBsYear = response.BsYear;
            ResultBsMonth = response.BsMonth;
            ResultBsDay = response.BsDay;
            ResultTithiLabel = response.TithiLabel;
            ResultNsYear = response.NsYear;
            ResultChandrama = response.Chandrama;
            ResultIsVerified = response.IsVerified == 1;
            HasResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Conversion failed: {ex.Message}";
            Debug.WriteLine($"DateConverterViewModel error: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Ensures the date is in YYYY-MM-DD format with zero-padded month and day.
    /// Accepts inputs like "2082-1-5" and normalizes to "2082-01-05".
    /// </summary>
    private static string NormalizeDate(string input)
    {
        var parts = input.Split('-');
        if (parts.Length != 3)
        {
            return input; // Let the API handle validation
        }

        var year = parts[0];
        var month = parts[1].PadLeft(2, '0');
        var day = parts[2].PadLeft(2, '0');
        return $"{year}-{month}-{day}";
    }
}
