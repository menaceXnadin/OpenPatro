using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenPatro.Models;

namespace OpenPatro.Services;

public sealed class HamroPatroClient
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HamroPatroParser _parser;

    public HamroPatroClient(HamroPatroParser parser)
    {
        _parser = parser;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0");
    }

    public static Uri GetMonthUri(int year, int month)
    {
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"https://www.hamropatro.com/calendar/{year}/{month}"), UriKind.Absolute);
    }

    public async Task<ParsedCalendarMonth> FetchMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var html = await _httpClient.GetStringAsync(GetMonthUri(year, month), cancellationToken);
        return _parser.ParseMonthHtml(year, month, html);
    }
}
