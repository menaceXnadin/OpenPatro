using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenPatro.Services;

public sealed class CalendarSyncService
{
    private readonly NepalTimeService _clock;
    private readonly CalendarRepository _calendarRepository;
    private readonly HamroPatroClient _hamroPatro;
    private readonly UserRepository _userRepository;

    public CalendarSyncService(NepalTimeService clock, CalendarRepository calendarRepository, HamroPatroClient hamroPatro, UserRepository userRepository)
    {
        _clock = clock;
        _calendarRepository = calendarRepository;
        _hamroPatro = hamroPatro;
        _userRepository = userRepository;
    }

    public async Task EnsureMonthPresentAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (await _calendarRepository.HasMonthAsync(year, month))
        {
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var parsed = await _hamroPatro.FetchMonthAsync(year, month, cts.Token);
            await _calendarRepository.UpsertMonthAsync(parsed);
        }
        catch (Exception)
        {
            // Network, timeout, or parse failure – month will be absent; the calendar shows an empty state.
        }
    }

    public async Task RunSilentRefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var today = await _calendarRepository.GetTodayAsync();
            if (today is null)
            {
                return;
            }

            var syncMarker = _clock.GetNepalToday().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var lastSyncMarker = await _userRepository.GetSettingAsync("CalendarSilentSyncDate");
            if (string.Equals(syncMarker, lastSyncMarker, StringComparison.Ordinal))
            {
                return;
            }

            await RefreshMonthAsync(today.BsYear, today.BsMonth, cancellationToken);
            var nextMonth = GetNextMonth(today.BsYear, today.BsMonth);
            await RefreshMonthAsync(nextMonth.year, nextMonth.month, cancellationToken);
            await _userRepository.SetSettingAsync("CalendarSilentSyncDate", syncMarker);
        }
        catch
        {
            // Silent background refresh by design.
        }
    }

    private async Task RefreshMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        var parsed = await _hamroPatro.FetchMonthAsync(year, month, cancellationToken);
        await _calendarRepository.UpsertMonthAsync(parsed);
    }

    public static (int year, int month) GetNextMonth(int year, int month)
    {
        return month == 12 ? (year + 1, 1) : (year, month + 1);
    }

    public static (int year, int month) GetPreviousMonth(int year, int month)
    {
        return month == 1 ? (year - 1, 12) : (year, month - 1);
    }
}