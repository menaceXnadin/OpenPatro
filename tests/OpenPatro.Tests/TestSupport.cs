using System.Net;
using Microsoft.Data.Sqlite;
using OpenPatro.Infrastructure;
using OpenPatro.Models;
using OpenPatro.Services;

namespace OpenPatro.Tests;

/// <summary>Records every request and answers with a canned body, optionally after a gate opens.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string> _body;

    public RecordingHandler(Func<HttpRequestMessage, string> body) => _body = body;

    public TaskCompletionSource? Gate { get; set; }

    public List<(string Url, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request.RequestUri!.ToString(), content));
        }

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body(request)) };
    }
}

/// <summary>ApplicationPaths pointed at a throwaway folder.</summary>
public sealed class TempPaths : ApplicationPaths, IDisposable
{
    public TempPaths()
        : base(Directory.CreateTempSubdirectory("openpatro-tests-").FullName)
    {
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(LocalFolderPath, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }
}

public static class Fixtures
{
    public static AppServices Services(
        ApplicationPaths? paths = null,
        CalendarRepository? calendar = null,
        BullionClient? bullion = null,
        ForexClient? forex = null)
    {
        return new AppServices(
            paths!, new NepalTimeService(), calendar!, null!, null!, null!, null!, null!, null!, null!, null!, bullion!, forex!);
    }

    public static CalendarDayRecord Day(int year, int month, int day, string adIso) => new()
    {
        BsYear = year,
        BsMonth = month,
        BsDay = day,
        BsDayText = day.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BsMonthName = "x",
        BsFullDate = "x",
        NepaliWeekday = "x",
        AdDateIso = adIso,
        AdDateText = "x",
        EventSummary = "--",
        Tithi = string.Empty,
        LunarText = string.Empty,
        Panchanga = string.Empty,
        DetailsPath = string.Empty,
        IsHoliday = false
    };

    /// <summary>Creates schema and a one-day month whose AD date is "today" in Nepal.</summary>
    public static async Task<(CalendarRepository Calendar, UserRepository User, string TodayIso)> SeedTodayAsync(
        ApplicationPaths paths, int bsYear = 2083, int bsMonth = 7, int bsDay = 24)
    {
        await new DatabaseBootstrapper(paths).InitializeAsync();
        var clock = new NepalTimeService();
        var calendar = new CalendarRepository(paths, clock);
        var todayIso = clock.GetNepalToday().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await calendar.UpsertMonthAsync(new ParsedCalendarMonth
        {
            RequestedYear = bsYear,
            RequestedMonth = bsMonth,
            TitleNepali = "t",
            TitleEnglish = "t",
            Days = new() { Day(bsYear, bsMonth, bsDay, todayIso) }
        });
        return (calendar, new UserRepository(paths), todayIso);
    }
}
