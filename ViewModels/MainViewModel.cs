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
        Settings = new SettingsViewModel(services);
    }

    public CalendarViewModel Calendar { get; }

    public SearchViewModel Search { get; }

    public SettingsViewModel Settings { get; }

    public ShellSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                RaisePropertyChanged(nameof(IsCalendarVisible));
                RaisePropertyChanged(nameof(IsSearchVisible));
                RaisePropertyChanged(nameof(IsSettingsVisible));
            }
        }
    }

    public bool IsCalendarVisible => SelectedSection == ShellSection.Calendar;

    public bool IsSearchVisible => SelectedSection == ShellSection.Search;

    public bool IsSettingsVisible => SelectedSection == ShellSection.Settings;

    public async Task InitializeAsync()
    {
        await Calendar.InitializeAsync();
        await Settings.InitializeAsync();
    }

    public async Task SelectCalendarDateAsync(int year, int month, int day)
    {
        SelectedSection = ShellSection.Calendar;
        await Calendar.SelectCalendarDateAsync(year, month, day);
    }
}