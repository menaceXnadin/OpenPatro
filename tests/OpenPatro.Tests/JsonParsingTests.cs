using OpenPatro.Services;
using Xunit;

namespace OpenPatro.Tests;

/// <summary>
/// Every API client now uses source-generated JSON (trim-safe). These pin that each one still
/// reads realistic payloads exactly as the old reflection-based serializer did.
/// </summary>
public class JsonParsingTests
{
    private static HttpClient Respond(string body) => new(new RecordingHandler(_ => body));

    [Fact]
    public async Task Forex_today_is_read_case_insensitively_with_string_decimals()
    {
        var forex = new ForexClient(Respond("""
            {"DATE":"2026-11-10","data":[{"Code":"USD","currency":"U.S. Dollar","buying":"141.25","selling":"141.85","unit":1,"type":"open"},
                                         {"code":"JPY","currency":"Japanese Yen","buying":"93.10","selling":"-","unit":10,"type":"open"}]}
            """));

        var today = (await forex.FetchTodayAsync())!;

        Assert.Equal("2026-11-10", today.Date);
        Assert.Equal(2, today.Data!.Count);
        Assert.Equal("USD", today.Data[0].Code);
        Assert.Equal(141.25m, today.Data[0].Buying);
        Assert.Equal(0m, today.Data[1].Selling);
        Assert.Equal(10, today.Data[1].Unit);
    }

    [Fact]
    public async Task Forex_history_request_and_response_round_trip()
    {
        var handler = new RecordingHandler(_ =>
            """[{"code":"USD","buying":"140.5","selling":"141.1","unit":1,"added_date":"2026-11-09"}]""");
        var forex = new ForexClient(new HttpClient(handler));

        var history = await forex.FetchHistoryAsync("USD", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10));

        Assert.Equal("""{"currency_code":"USD","from":"2026-11-01","to":"2026-11-10"}""", handler.Requests.Single().Body);
        var entry = Assert.Single(history);
        Assert.Equal(140.5m, entry.Buying);
        Assert.Equal("2026-11-09", entry.AddedDate);
    }

    [Fact]
    public async Task Bullion_reads_date_keyed_entries()
    {
        var bullion = new BullionClient(Respond("""
            {"source":"Fenegosida","data":{"2026-11-09":{"bs":"24.07.2083","t_ha":"150,000","t_te":"149000","t_s":"2000","g_ha":128600,"g_te":"127750","g_s":"-"}}}
            """));

        var response = (await bullion.FetchAsync())!;

        var day = response.Entries["2026-11-09"];
        Assert.Equal("24.07.2083", day.Bs);
        Assert.Equal(150_000m, day.GoldHallmarkPerTola);
        Assert.Equal(128_600m, day.GoldHallmarkPer10g);
        Assert.Equal(0m, day.SilverPer10g);
    }

    [Fact]
    public async Task Stock_market_home_data_is_read()
    {
        var client = new ShareHubNepseClient(Respond("""
            {"marketStatus":{"status":"OPEN","time":"2026-11-10T09:00:00"},
             "stockSummary":{"advanced":120,"declined":80,"unchanged":10,"positiveCircuit":2,"negativeCircuit":1},
             "indices":[{"symbol":"NEPSE","name":"NEPSE Index","currentValue":"2650.12","change":"12.5","changePercent":"0.47"}],
             "liveCompanyData":[{"symbol":"NABIL","securityName":"Nabil Bank","totalTradeQuantity":"1000","totalTradeValue":"500000","companyLogo":"nabil.png"}],
             "demand":[1,2,3],"supply":[]}
            """));

        var data = (await client.FetchHomePageDataAsync())!;

        Assert.Equal("OPEN", data.MarketStatus!.Status);
        Assert.Equal(120, data.StockSummary!.Advanced);
        Assert.Equal(2650.12m, data.Indices!.Single().CurrentValue);
        var company = data.LiveCompanyData!.Single();
        Assert.Equal(1000m, company.TotalTradeQuantity);
        Assert.Equal("nabil.png", company.LogoPath);
        Assert.Equal(3, data.Demand.GetArrayLength());
    }

    [Fact]
    public async Task Rashifal_and_date_convert_are_read()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.Contains("dateConvert", StringComparison.Ordinal)
            ? """{"ad":"2026-11-10","bs":"24.07.2083","bs_day":24,"bs_month":7,"bs_year":2083,"tithi":5,"ns_year":1146,"chandrama":7,"is_verified":1}"""
            : """{"np":[{"type":"D","title":"आजको राशिफल","todate":"2083-07-24","aries":"शुभ दिन"}]}""");
        var api = new NepaliPatroApiClient(new HttpClient(handler));

        var rashifal = (await api.FetchRashifalAsync())!;
        Assert.Equal("शुभ दिन", rashifal.Np.Single().GetPrediction("aries"));

        var converted = (await api.ConvertDateAsync(new OpenPatro.Models.DateConvertRequest { Date = "2083-07-24", BasedOn = "BS" }))!;
        Assert.Equal("""{"date":"2083-07-24","based_on":"BS"}""", handler.Requests[^1].Body);
        Assert.Equal("2026-11-10", converted.Ad);
        Assert.Equal(2083, converted.BsYear);
        Assert.Equal(1, converted.IsVerified);
    }

    [Fact]
    public void HamroPatro_bootstrap_json_is_parsed()
    {
        // 2026-11-10 is a Tuesday (dayOfWeek 2).
        const string html = """
            <html><script id="hp-bootstrap" type="application/json">
            {"data":{"calendar":{"initialMonths":[{"yearBs":2083,"monthBs":7,"monthNameNp":"कार्तिक","yearBsNp":"२०८३","daysInMonth":1,
              "weekdayHeaders":[{"np":"आइत"},{"np":"सोम"},{"np":"मंगल"},{"np":"बुध"},{"np":"बिहि"},{"np":"शुक्र"},{"np":"शनि"}],
              "days":[{"year_ad":2026,"month_ad":11,"day_ad":10,"year_bs":2083,"month_bs":7,"day_bs":24,"dayOfWeek":2,"inMonth":true,
                       "dayBsNp":"२४","tithiShort":"पञ्चमी","panchanga":{"month":6,"paksha":0,"yoga":1,"karana":2,"nakshtra":3,"monthType":0},
                       "patroEvent":{"isHoliday":true,"items":[{"titleNp":"छठ पर्व"}]}}]}]}}}
            </script></html>
            """;

        var month = new HamroPatroParser().ParseMonthHtml(2083, 7, html);

        var day = Assert.Single(month.Days);
        Assert.Equal("2026-11-10", day.AdDateIso);
        Assert.Equal("मंगल", day.NepaliWeekday);
        Assert.Equal("छठ पर्व", day.EventSummary);
        Assert.True(day.IsHoliday);
        Assert.Equal("२०८३ कार्तिक", month.TitleNepali);
    }
}
