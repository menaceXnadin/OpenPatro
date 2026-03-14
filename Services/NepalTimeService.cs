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

    public IDisposable ScheduleDailyMidnightCallback(Action callback)
    {
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            callback();
            timer?.Change(GetDelayUntilNextMidnight(), Timeout.InfiniteTimeSpan);
        }, null, GetDelayUntilNextMidnight(), Timeout.InfiniteTimeSpan);

        return timer;
    }

    private TimeSpan GetDelayUntilNextMidnight()
    {
        var now = GetNepalNow();
        var nextMidnightLocal = now.Date.AddDays(1);
        var nextMidnightOffset = new DateTimeOffset(nextMidnightLocal, now.Offset);
        var delay = nextMidnightOffset.ToUniversalTime() - DateTimeOffset.UtcNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1);
    }
}