using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenPatro.Models;

namespace OpenPatro.Services;

public sealed class HamroPatroClient
{
    private static readonly Uri BaseUri = new("https://www.hamropatro.com/gui/home/calender-ajax.php", UriKind.Absolute);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HamroPatroParser _parser;

    public HamroPatroClient(HamroPatroParser parser)
    {
        _parser = parser;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0");
    }

    public async Task<ParsedCalendarMonth> FetchMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"{BaseUri}?year={year}&month={month}");
        var html = await _httpClient.GetStringAsync(uri, cancellationToken);
        return _parser.ParseMonthHtml(year, month, html);
    }
}