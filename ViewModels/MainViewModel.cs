using System.Threading.Tasks;
using OpenPatro.Infrastructure;

namespace OpenPatro.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private ShellSection _selectedSection = ShellSection.Calendar;

    public MainViewModel(AppServices services)
    {
        Calendar = new CalendarViewModel(services);
        Search = new SearchViewModel(services);
        StockMarket = new StockMarketViewModel(services);
        Settings = new SettingsViewModel(services);
        Rashifal = new RashifalViewModel(services);
        ShubhaSait = new ShubhaSaitViewModel(services);
        DateConverter = new DateConverterViewModel(services);
        Bullion = new BullionViewModel(services);
    }

    public CalendarViewModel Calendar { get; }

    public SearchViewModel Search { get; }

    public StockMarketViewModel StockMarket { get; }

    public SettingsViewModel Settings { get; }

    public RashifalViewModel Rashifal { get; }

    public ShubhaSaitViewModel ShubhaSait { get; }

    public DateConverterViewModel DateConverter { get; }

    public BullionViewModel Bullion { get; }

    public ShellSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value == ShellSection.Search)
            {
                value = ShellSection.Calendar;
            }

            if (SetProperty(ref _selectedSection, value))
            {
                RaisePropertyChanged(nameof(IsCalendarVisible));
                RaisePropertyChanged(nameof(IsSearchVisible));
                RaisePropertyChanged(nameof(IsStockMarketVisible));
                RaisePropertyChanged(nameof(IsSettingsVisible));
                RaisePropertyChanged(nameof(IsRashifalVisible));
                RaisePropertyChanged(nameof(IsShubhaSaitVisible));
                RaisePropertyChanged(nameof(IsDateConverterVisible));
                RaisePropertyChanged(nameof(IsBullionVisible));
            }
        }
    }

    public bool IsCalendarVisible => SelectedSection == ShellSection.Calendar;

    public bool IsSearchVisible => false;

    public bool IsStockMarketVisible => SelectedSection == ShellSection.StockMarket;

    public bool IsSettingsVisible => SelectedSection == ShellSection.Settings;

    public bool IsRashifalVisible => SelectedSection == ShellSection.Rashifal;

    public bool IsShubhaSaitVisible => SelectedSection == ShellSection.ShubhaSait;

    public bool IsDateConverterVisible => SelectedSection == ShellSection.DateConverter;

    public bool IsBullionVisible => SelectedSection == ShellSection.Bullion;

    public async Task InitializeAsync()
    {
        SelectedSection = ShellSection.Calendar;
        await Calendar.InitializeAsync();
        await Settings.InitializeAsync();

        // Fire-and-forget: pre-fetch Rashifal and Shubha Sait data in the background.
        // These run on the UI thread (the HTTP calls are async/non-blocking),
        // so ObservableCollection mutations are safe for WinUI data bindings.
        _ = SafeInitializeAsync(Rashifal);
        _ = SafeInitializeAsync(ShubhaSait);
        _ = SafeInitializeAsync(StockMarket);
        _ = SafeInitializeAsync(Bullion);
    }

    public async Task SelectCalendarDateAsync(int year, int month, int day)
    {
        SelectedSection = ShellSection.Calendar;
        await Calendar.SelectCalendarDateAsync(year, month, day);
    }

    private static async Task SafeInitializeAsync(RashifalViewModel vm)
    {
        try { await vm.InitializeAsync(); } catch { /* Network failure during pre-fetch is OK */ }
    }

    private static async Task SafeInitializeAsync(ShubhaSaitViewModel vm)
    {
        try { await vm.InitializeAsync(); } catch { /* Network failure during pre-fetch is OK */ }
    }

    private static async Task SafeInitializeAsync(StockMarketViewModel vm)
    {
        try { await vm.InitializeAsync(); } catch { /* Network failure during pre-fetch is OK */ }
    }

    private static async Task SafeInitializeAsync(BullionViewModel vm)
    {
        try { await vm.InitializeAsync(); } catch { /* Network failure during pre-fetch is OK */ }
    }
}