using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenPatro.Models;

namespace OpenPatro.Services;

/// <summary>
/// Parses hamropatro.com/calendar/{year}/{month}. The page embeds its month grid as JSON in
/// &lt;script id="hp-bootstrap"&gt; under data.calendar.initialMonths (previous, requested and next month).
/// </summary>
public sealed partial class HamroPatroParser
{
    // Name tables mirror the ones in Hamro Patro's own JS bundle; panchanga fields are indices into them.
    private static readonly string[] LunarMonthNames = { "वैशाख", "जेठ", "असार", "साउन", "भदौ", "असोज", "कार्तिक", "मंसिर", "पुष", "माघ", "फागुन", "चैत" };
    private static readonly string[] PakshaNames = { "शुक्ल", "कृष्ण" };
    private static readonly string[] YogaNames = { "विषकुम्भ", "प्रीति", "आयुष्मान", "सौभाग्य", "शोभन", "अतिगंड", "सुकर्मा", "धृति", "शूल", "गण्ड", "वृद्धि", "ध्रुव", "व्याघात", "हर्षण", "वज्र", "सिद्धि", "व्यतिपात", "वरियान", "परिधि", "शिव", "सिद्ध", "साध्य", "शुभ", "शुक्ल", "ब्रह्म", "एन्द्र", "वैधृति" };
    private static readonly string[] KaranaNames = { "बव", "बालव", "कौलव", "तैतिल", "गर", "वणिज", "विष्टि/भद्रा", "शकुनि", "चतुष्पद", "नाग", "किंस्तुघ्न" };
    private static readonly string[] NakshatraNames = { "अश्विनी", "भरणी", "कृत्तिका", "रोहिणी", "मृगशिरा", "आर्द्रा", "पुनर्वसु", "पुष्य", "अश्लेषा", "मघा", "पूर्वफल्गुनी", "उत्तरफल्गुनी", "हस्ता", "चित्रा", "स्वाति", "विशाखा", "अनुराधा", "ज्येष्ठा", "मूल", "पूर्वाषाढा", "उत्तरषाढा", "श्रवण", "धनिष्ठा", "शतभिषा", "पूर्वभाद्र", "उत्तरभाद्र", "रेवती" };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ParsedCalendarMonth ParseMonthHtml(int requestedYear, int requestedMonth, string html)
    {
        var month = ExtractInitialMonths(html).FirstOrDefault(m => m.YearBs == requestedYear && m.MonthBs == requestedMonth)
            ?? throw new InvalidOperationException($"Month {requestedYear}/{requestedMonth} not found in calendar data.");

        var inMonthDays = month.Days.Where(static day => day.InMonth).OrderBy(static day => day.DayBs).ToList();
        if (inMonthDays.Count == 0 || inMonthDays.Count != month.DaysInMonth)
        {
            throw new InvalidOperationException($"Expected {month.DaysInMonth} days for {requestedYear}/{requestedMonth}, found {inMonthDays.Count}.");
        }

        var days = inMonthDays.Select(day => ParseDay(month, day)).ToList();
        var firstAd = inMonthDays[0].AdDate;
        var lastAd = inMonthDays[^1].AdDate;
        var englishMonths = $"{firstAd.ToString("MMM", CultureInfo.InvariantCulture)}/{lastAd.ToString("MMM", CultureInfo.InvariantCulture)}";

        return new ParsedCalendarMonth
        {
            RequestedYear = requestedYear,
            RequestedMonth = requestedMonth,
            TitleNepali = $"{month.YearBsNp} {month.MonthNameNp}",
            TitleEnglish = firstAd.Year == lastAd.Year ? $"{englishMonths} {firstAd.Year}" : $"{englishMonths} {firstAd.Year}-{lastAd.Year}",
            Days = days
        };
    }

    private static List<HpMonth> ExtractInitialMonths(string html)
    {
        var match = BootstrapScriptRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("Calendar bootstrap data not found.");
        }

        using var document = JsonDocument.Parse(match.Groups[1].Value);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("calendar", out var calendar)
            || !calendar.TryGetProperty("initialMonths", out var initialMonths))
        {
            throw new InvalidOperationException("Calendar months missing from bootstrap data.");
        }

        return initialMonths.Deserialize<List<HpMonth>>(JsonOptions) ?? throw new InvalidOperationException("Calendar months could not be read.");
    }

    private static CalendarDayRecord ParseDay(HpMonth month, HpDay day)
    {
        var adDate = day.AdDate;
        if ((int)adDate.DayOfWeek != day.DayOfWeek || day.DayOfWeek >= month.WeekdayHeaders.Count)
        {
            throw new InvalidOperationException(
                $"Weekday mismatch for BS {day.YearBs}/{day.MonthBs}/{day.DayBs}: " +
                $"page says {day.DayOfWeek} but AD date {adDate:yyyy-MM-dd} is a {adDate.DayOfWeek}.");
        }

        var nepaliWeekday = month.WeekdayHeaders[day.DayOfWeek].Np;
        var events = day.PatroEvent?.Items.Select(static item => CleanText(item.TitleNp)).Where(static title => title.Length > 0).ToList() ?? new List<string>();
        var tithi = CleanText(day.TithiShort);
        var panchanga = day.Panchanga;

        return new CalendarDayRecord
        {
            BsYear = day.YearBs,
            BsMonth = day.MonthBs,
            BsDay = day.DayBs,
            BsDayText = day.DayBsNp,
            BsMonthName = month.MonthNameNp,
            BsFullDate = $"{day.DayBsNp} {month.MonthNameNp} {month.YearBsNp}, {nepaliWeekday}",
            NepaliWeekday = nepaliWeekday,
            AdDateIso = adDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AdDateText = adDate.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture),
            EventSummary = events.Count > 0 ? string.Join('/', events) : "--",
            Tithi = tithi,
            LunarText = panchanga is null
                ? "--"
                : $"{(panchanga.MonthType == 1 ? "अधिक " : string.Empty)}{Lookup(LunarMonthNames, panchanga.Month)} {Lookup(PakshaNames, panchanga.Paksha)} {tithi}".Trim(),
            Panchanga = panchanga is null
                ? string.Empty
                : $"पञ्चाङ्ग: {Lookup(YogaNames, panchanga.Yoga)} {Lookup(KaranaNames, panchanga.Karana)} {Lookup(NakshatraNames, panchanga.Nakshtra)}",
            DetailsPath = $"/date/{day.YearBs}-{day.MonthBs}-{day.DayBs}",
            // Event-level flag only: the day-level isHoliday also covers weekends, which the UI colours itself.
            IsHoliday = day.PatroEvent?.IsHoliday == true
        };
    }

    private static string Lookup(string[] names, int index)
    {
        return (uint)index < (uint)names.Length ? names[index] : string.Empty;
    }

    private static string CleanText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : WhitespaceRegex().Replace(value, " ").Trim();
    }

    [GeneratedRegex(@"<script[^>]*\bid=""hp-bootstrap""[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex BootstrapScriptRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed class HpMonth
    {
        public int YearBs { get; set; }

        public int MonthBs { get; set; }

        public string MonthNameNp { get; set; } = string.Empty;

        public string YearBsNp { get; set; } = string.Empty;

        public int DaysInMonth { get; set; }

        public List<HpWeekdayHeader> WeekdayHeaders { get; set; } = new();

        public List<HpDay> Days { get; set; } = new();
    }

    private sealed class HpWeekdayHeader
    {
        public string Np { get; set; } = string.Empty;
    }

    private sealed class HpDay
    {
        [JsonPropertyName("year_ad")]
        public int YearAd { get; set; }

        [JsonPropertyName("month_ad")]
        public int MonthAd { get; set; }

        [JsonPropertyName("day_ad")]
        public int DayAd { get; set; }

        [JsonPropertyName("year_bs")]
        public int YearBs { get; set; }

        [JsonPropertyName("month_bs")]
        public int MonthBs { get; set; }

        [JsonPropertyName("day_bs")]
        public int DayBs { get; set; }

        public int DayOfWeek { get; set; }

        public bool InMonth { get; set; }

        public string DayBsNp { get; set; } = string.Empty;

        public string? TithiShort { get; set; }

        public HpPanchanga? Panchanga { get; set; }

        public HpPatroEvent? PatroEvent { get; set; }

        public DateOnly AdDate => new(YearAd, MonthAd, DayAd);
    }

    private sealed class HpPanchanga
    {
        public int Nakshtra { get; set; }

        public int Month { get; set; }

        public int MonthType { get; set; }

        public int Paksha { get; set; }

        public int Yoga { get; set; }

        public int Karana { get; set; }
    }

    private sealed class HpPatroEvent
    {
        public bool IsHoliday { get; set; }

        public List<HpEventItem> Items { get; set; } = new();
    }

    private sealed class HpEventItem
    {
        public string? TitleNp { get; set; }
    }
}
