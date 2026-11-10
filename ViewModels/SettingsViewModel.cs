using System;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenPatro.Infrastructure;
using OpenPatro.Services;

namespace OpenPatro.ViewModels;

public sealed class SettingsViewModel : BindableBase
{
    private readonly AppServices _services;
    private bool _startWithWindows;
    private bool _isApplyingStartup;
    private string _lastSyncLabel = "Not yet synced in this session";

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public ICommand RefreshCommand { get; }

    public string CalendarDatabasePath => _services.Paths.CalendarDatabasePath;

    public string UserDatabasePath => _services.Paths.UserDatabasePath;

    /// <summary>
    /// Bound TwoWay to the Settings toggle. Setting it applies the change; the toggle
    /// snaps back if Windows refused it (e.g. MSIX startup disabled in Task Manager).
    /// </summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value) && !_isApplyingStartup)
            {
                _ = ApplyStartWithWindowsAsync(value);
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
        await RefreshStartupStateAsync();
        await RefreshLastSyncLabelAsync();
    }

    /// <summary>Re-reads the real autostart state (e.g. after the tray menu changed it).</summary>
    public async Task RefreshStartupStateAsync()
    {
        bool enabled;
        try
        {
            enabled = await _services.Startup.IsEnabledAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Settings: reading startup state FAILED: {ex}");
            return;
        }

        SetStartWithWindowsSilently(enabled);
    }

    private async Task ApplyStartWithWindowsAsync(bool requested)
    {
        bool actual;
        try
        {
            actual = await _services.Startup.SetEnabledAsync(requested);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Settings: changing startup state FAILED: {ex}");
            actual = !requested;
        }

        SetStartWithWindowsSilently(actual);
        await ((App)Microsoft.UI.Xaml.Application.Current).RefreshTrayPresentationAsync();
    }

    private void SetStartWithWindowsSilently(bool value)
    {
        _isApplyingStartup = true;
        try
        {
            StartWithWindows = value;
        }
        finally
        {
            _isApplyingStartup = false;
        }
    }

    private async Task RefreshLastSyncLabelAsync()
    {
        var marker = await _services.UserRepository.GetSettingAsync(CalendarSyncService.SilentSyncSettingKey);
        LastSyncLabel = string.IsNullOrWhiteSpace(marker) ? "No background refresh recorded yet" : $"Last refresh: {marker}";
    }

    private async Task RefreshAsync()
    {
        // The user explicitly asked for fresh data, so bypass the once-a-day guard.
        var refreshed = await _services.CalendarSync.RunSilentRefreshAsync(force: true);
        await RefreshLastSyncLabelAsync();
        if (!refreshed)
        {
            LastSyncLabel += " (refresh failed — check your internet connection)";
        }

        await ((App)Microsoft.UI.Xaml.Application.Current).RefreshTrayPresentationAsync();
    }
}
