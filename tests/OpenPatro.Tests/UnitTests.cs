using System.Text.Json;
using OpenPatro.Models;
using OpenPatro.Services;
using Xunit;

namespace OpenPatro.Tests;

/// <summary>Pure-logic units that were already sound; pinned so refactors keep them that way.</summary>
public class UnitTests
{
    [Theory]
    [InlineData("1,234.50", 1234.50)]
    [InlineData("-", 0)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    public void SafeDecimal_parses_api_strings(string raw, double expected)
    {
        var entry = JsonSerializer.Deserialize<ForexRateEntry>($$"""{"buying":"{{raw}}"}""")!;
        Assert.Equal((decimal)expected, entry.Buying);
    }

    [Fact]
    public void SafeDecimal_accepts_json_numbers()
    {
        Assert.Equal(12.5m, JsonSerializer.Deserialize<ForexRateEntry>("""{"buying":12.5}""")!.Buying);
    }

    [Theory]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData(@"c:\x", @"c:\\x")]
    public void Like_patterns_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, CalendarRepository.EscapeLikePattern(input));
    }

    [Fact]
    public void Calendar_grid_fits_both_dimensions()
    {
        var metrics = WindowLayoutService.ComputeCalendarGridMetrics(availableWidth: 700, availableHeight: 2000, weekdayHeaderHeight: 40)!;

        Assert.Equal(100, metrics.ItemWidth);           // width-bound: 700 / 7
        Assert.Equal(92, metrics.ItemHeight);           // 0.92 aspect
        Assert.Equal(704, metrics.GridWidth);           // + 4px buffer
        Assert.Null(WindowLayoutService.ComputeCalendarGridMetrics(0, 500, 40));
    }

    [Theory]
    [InlineData(2082, 12, 2083, 1)]
    [InlineData(2083, 1, 2083, 2)]
    public void Next_month_wraps_the_year(int y, int m, int ey, int em)
    {
        Assert.Equal((ey, em), CalendarSyncService.GetNextMonth(y, m));
        Assert.Equal((y, m), CalendarSyncService.GetPreviousMonth(ey, em));
    }
}
