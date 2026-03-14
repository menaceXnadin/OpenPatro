using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OpenPatro.Services;

public sealed class BundledCalendarSeedService
{
    private readonly ApplicationPaths _paths;
    private readonly HamroPatroParser _parser;

    public BundledCalendarSeedService(ApplicationPaths paths, HamroPatroParser parser)
    {
        _paths = paths;
        _parser = parser;
    }

    public async Task RunAsync()
    {
        var outputDirectory = Path.GetDirectoryName(_paths.BundledCalendarDatabasePath)!;
        Directory.CreateDirectory(outputDirectory);

        if (File.Exists(_paths.BundledCalendarDatabasePath))
        {
            File.Delete(_paths.BundledCalendarDatabasePath);
        }

        using (var file = File.Create(_paths.BundledCalendarDatabasePath))
        {
        }

        var seedingPaths = new ApplicationPathsSeederShim(_paths.BundledCalendarDatabasePath, _paths.UserDatabasePath);
        var bootstrapper = new DatabaseBootstrapper(seedingPaths);
        await bootstrapper.InitializeAsync();

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0 Seeder");
        var repository = new CalendarRepository(seedingPaths, new NepalTimeService());

        for (var year = 2000; year <= 2089; year++)
        {
            for (var month = 1; month <= 12; month++)
            {
                var html = await httpClient.GetStringAsync($"https://www.hamropatro.com/gui/home/calender-ajax.php?year={year}&month={month}");
                var parsed = _parser.ParseMonthHtml(year, month, html);
                await repository.UpsertMonthAsync(parsed);
            }
        }
    }

    private sealed class ApplicationPathsSeederShim : ApplicationPaths
    {
        public ApplicationPathsSeederShim(string calendarPath, string userPath)
            : base(Path.GetDirectoryName(calendarPath)!)
        {
            CalendarDatabasePath = calendarPath;
            UserDatabasePath = userPath;
            BundledCalendarDatabasePath = calendarPath;
        }
    }
}