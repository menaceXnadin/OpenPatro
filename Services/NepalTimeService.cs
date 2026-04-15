using System;
using System.Threading;

namespace OpenPatro.Services;

public sealed class NepalTimeService
{
    private static readonly TimeSpan NepalOffset = TimeSpan.FromMinutes(345);
    private readonly TimeZoneInfo _timeZone;

    public NepalTimeService()
    {
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById("Nepal Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            _timeZone = TimeZoneInfo.CreateCustomTimeZone("Nepal Custom Time", NepalOffset, "Nepal Standard Time", "Nepal Standard Time");
        }
    }

    public DateTimeOffset GetNepalNow() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);

    public DateOnly GetNepalToday() => DateOnly.FromDateTime(GetNepalNow().Date);

    /// <summary>
    /// Starts a 1-minute polling timer that invokes <paramref name="callback"/> whenever
    /// the Nepal date changes (e.g. at midnight, or after the system wakes from sleep).
    /// Returns a <see cref="DateChangeWatcher"/> that can also be poked immediately
    /// from a <c>WM_TIMECHANGE</c> WndProc message so manual clock adjustments are
    /// caught without waiting for the next poll tick.
    /// Dispose the watcher to stop polling.
    /// </summary>
    public DateChangeWatcher WatchForDateChange(Action callback)
        => new DateChangeWatcher(this, callback);
}

/// <summary>
/// Watches for Nepal date changes via a 1-minute poll and optionally an immediate
/// <c>WM_TIMECHANGE</c> nudge. Thread-safe: uses <see cref="Interlocked"/> to
/// prevent double-fires when the timer and a WM_TIMECHANGE arrive simultaneously.
/// </summary>
public sealed class DateChangeWatcher : IDisposable
{
    private readonly Action _callback;
    private readonly NepalTimeService _clock;
    // Stored as long[] so Interlocked.Exchange can operate on the element by ref.
    private readonly long[] _lastKnownDayNumber;
    private readonly Timer _timer;
    private int _disposed;

    internal DateChangeWatcher(NepalTimeService clock, Action callback)
    {
        _clock = clock;
        _callback = callback;
        _lastKnownDayNumber = new long[] { (long)clock.GetNepalToday().DayNumber };
        _timer = new Timer(_ => Check(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Call this from a <c>WM_TIMECHANGE</c> WndProc handler to trigger an immediate
    /// date check outside the normal 1-minute poll cycle.
    /// </summary>
    public void OnSystemTimeChanged() => Check();

    private void Check()
    {
        if (_disposed != 0)
        {
            return;
        }

        var todayNumber = (long)_clock.GetNepalToday().DayNumber;
        var previous = Interlocked.Exchange(ref _lastKnownDayNumber[0], todayNumber);
        if (previous != todayNumber)
        {
            _callback();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _timer.Dispose();
        }
    }
}
