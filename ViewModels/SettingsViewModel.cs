using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Services;

namespace OpenPatro.ViewModels;

public sealed class SettingsViewModel : BindableBase
{
    private readonly AppServices _services;
    private bool _startWithWindows;
    private string _lastSyncLabel = "Not yet synced in this session";

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public ICommand RefreshCommand { get; }

    public string CalendarDatabasePath => _services.Paths.CalendarDatabasePath;

    public string UserDatabasePath => _services.Paths.UserDatabasePath;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _services.Startup.SetEnabled(value);
            }
        }
    }

    public string LastSyncLabel
    {
        get => _lastSyncLabel;
        private set => SetProperty(ref _lastSyncLabel, value);
    }

    public async Task InitializeAsync()
    {
        StartWithWindows = _services.Startup.IsEnabled();
        var marker = await _services.UserRepository.GetSettingAsync("CalendarSilentSyncDate");
        LastSyncLabel = string.IsNullOrWhiteSpace(marker) ? "No background refresh recorded yet" : $"Last silent refresh: {marker}";
    }

    private async Task RefreshAsync()
    {
        await _services.CalendarSync.RunSilentRefreshAsync();
        await InitializeAsync();
        await ((App)Microsoft.UI.Xaml.Application.Current).RefreshTrayPresentationAsync();
    }
}