using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenPatro.Models;
using OpenPatro.Services;
using OpenPatro.ViewModels;
using Xunit;

namespace OpenPatro.Tests;

/// <summary>Regression tests for the bugs confirmed in AUDIT_REPORT.md.</summary>
public class RegressionTests
{
    // Bug 2: Live Market VOLUME showed turnover and TURNOVER showed volume.
    [Fact]
    public void LiveMarket_volume_and_turnover_text_match_their_columns()
    {
        var vm = new StockMarketViewModel(null!);
        vm.MapResponse(new StockMarketHomeData
        {
            LiveCompanyData = new()
            {
                new LiveCompanyDataInfo { Symbol = "NABIL", SecurityName = "Nabil Bank", TotalTradeQuantity = 1_000m, TotalTradeValue = 500_000m }
            }
        });

        var row = vm.FilteredLiveCompanies.Single();
        Assert.Equal("1,000.00", row.Quantity);   // MainWindow.xaml: VOLUME column
        Assert.Equal("500,000.00", row.Turnover); // MainWindow.xaml: TURNOVER column
        Assert.Equal(1_000m, row.QuantityValue);
        Assert.Equal(500_000m, row.TurnoverValue);
    }

    [Fact]
    public void LiveMarket_sorting_by_volume_orders_by_quantity()
    {
        var vm = new StockMarketViewModel(null!);
        vm.MapResponse(new StockMarketHomeData
        {
            LiveCompanyData = new()
            {
                new LiveCompanyDataInfo { Symbol = "B", TotalTradeQuantity = 20m, TotalTradeValue = 1_000m },
                new LiveCompanyDataInfo { Symbol = "A", TotalTradeQuantity = 10m, TotalTradeValue = 9_000m },
            }
        });

        vm.SortLiveCompaniesBy("Volume");

        Assert.Equal(new[] { "A", "B" }, vm.FilteredLiveCompanies.Select(r => r.Symbol));
    }

    // Bug 6: Settings "Refresh current data now" did nothing after the first sync of the day.
    [Fact]
    public async Task Forced_refresh_downloads_even_if_already_synced_today()
    {
        using var paths = new TempPaths();
        var (calendar, user, todayIso) = await Fixtures.SeedTodayAsync(paths);
        await user.SetSettingAsync(CalendarSyncService.SilentSyncSettingKey, todayIso);

        var handler = new RecordingHandler(_ => "<html></html>"); // parse fails => refresh reports failure
        var sync = new CalendarSyncService(
            new NepalTimeService(), calendar, new HamroPatroClient(new HamroPatroParser(), new HttpClient(handler)), user);

        var automatic = await sync.RunSilentRefreshAsync();
        Assert.True(automatic);
        Assert.Empty(handler.Requests); // the automatic refresh still runs at most once a day

        var forced = await sync.RunSilentRefreshAsync(force: true);
        Assert.NotEmpty(handler.Requests);
        Assert.False(forced); // and a failed forced refresh is reported, not swallowed
    }

    // Bug 3: a corrupt user.db made startup fail on every launch.
    [Fact]
    public async Task Bootstrapper_recovers_from_a_corrupt_user_db()
    {
        using var paths = new TempPaths();
        await File.WriteAllTextAsync(paths.UserDatabasePath, "this is not a sqlite database, it is long enough to have a header");

        await new DatabaseBootstrapper(paths).InitializeAsync();

        var user = new UserRepository(paths);
        await user.SetSettingAsync("k", "v");
        Assert.Equal("v", await user.GetSettingAsync("k"));
        Assert.Single(Directory.GetFiles(paths.LocalFolderPath, "user.db.corrupt-*"));
    }

    [Fact]
    public async Task Bootstrapper_recovers_from_a_corrupt_calendar_db()
    {
        using var paths = new TempPaths();
        await File.WriteAllTextAsync(paths.CalendarDatabasePath, "this is not a sqlite database, it is long enough to have a header");

        await new DatabaseBootstrapper(paths).InitializeAsync();

        var calendar = new CalendarRepository(paths, new NepalTimeService());
        Assert.False(await calendar.HasMonthAsync(1, 1)); // schema exists and is queryable
    }

    // Bug 4: the date converter pre-filled today's AD date into the BS fields.
    [Fact]
    public async Task DateConverter_defaults_to_todays_date_in_the_selected_calendar()
    {
        using var paths = new TempPaths();
        var (calendar, _, todayIso) = await Fixtures.SeedTodayAsync(paths, bsYear: 2083, bsMonth: 7, bsDay: 24);
        var vm = new DateConverterViewModel(Fixtures.Services(paths, calendar));

        await vm.EnsureDefaultInputAsync();
        Assert.Equal("BS", vm.ConversionDirection);
        Assert.Equal("2083-07-24", vm.InputDate);

        await vm.SetDirectionAsync("AD");
        Assert.Equal(todayIso, vm.InputDate);

        await vm.SetDirectionAsync("BS");
        Assert.Equal("2083-07-24", vm.InputDate);
    }

    // Bug 8: the Bullion page size (120) was not a picker option.
    [Fact]
    public void Bullion_default_page_size_is_a_selectable_option()
    {
        int[] pickerOptions = { 25, 50, 100, 200, 500 }; // MainWindow.xaml BullionItemsPerPagePicker
        Assert.Contains(new BullionViewModel(null!).ItemsPerPage, pickerOptions);
    }

    // Bug 7: choosing a range while the previous range was loading was lost.
    [Fact]
    public async Task Bullion_range_change_during_load_loads_the_new_range()
    {
        var handler = new RecordingHandler(_ =>
            """{"source":"x","data":{"2026-11-09":{"bs":"24.07.2083","t_ha":"150000","t_te":"149000","t_s":"2000","g_ha":"1","g_te":"1","g_s":"1"}}}""")
        {
            Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var vm = new BullionViewModel(Fixtures.Services(bullion: new BullionClient(new HttpClient(handler))));

        var initialLoad = vm.InitializeAsync();   // 30D load in flight
        var rangeLoad = vm.ApplyRangeAsync("1Y"); // user picks "Last 1 year" meanwhile
        handler.Gate.SetResult();
        await Task.WhenAll(initialLoad, rangeLoad);

        Assert.Equal("1Y", vm.SelectedRangeKey);
        Assert.False(vm.IsBusy);
        Assert.True(vm.HasData);
        Assert.Equal(2, handler.Requests.Count);
        var oneYearAgo = new NepalTimeService().GetNepalToday().AddYears(-1);
        Assert.Contains($"from-date={oneYearAgo.Year}-{oneYearAgo.Month}-{oneYearAgo.Day}", handler.Requests[^1].Url);
    }

    // Bug 9: Forex history dates followed the system calendar (e.g. Thai Buddhist era).
    [Fact]
    public async Task Forex_history_dates_are_gregorian_regardless_of_culture()
    {
        var handler = new RecordingHandler(_ => "[]");
        var forex = new ForexClient(new HttpClient(handler));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            await forex.FetchHistoryAsync("USD", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var body = handler.Requests.Single().Body!;
        Assert.Contains("\"from\":\"2026-11-01\"", body);
        Assert.Contains("\"to\":\"2026-11-10\"", body);
    }

    // Window bounds are now saved in one transaction.
    [Fact]
    public async Task WindowBounds_round_trip()
    {
        using var paths = new TempPaths();
        await new DatabaseBootstrapper(paths).InitializeAsync();
        var bounds = new WindowBoundsService(new UserRepository(paths));

        await bounds.SaveAsync(10, 20, 1200, 900, isMaximized: true);

        Assert.Equal(new SavedWindowBounds(10, 20, 1200, 900, true), await bounds.LoadAsync());
    }
}
