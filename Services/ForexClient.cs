using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenPatro.Models;

namespace OpenPatro.Services;

/// <summary>
/// HTTP client for the NepaliPatro forex API.
/// - Today's rates:  GET  https://api.nepalipatro.com.np/forex?web=true
/// - History:        POST https://api.nepalipatro.com.np/forex/currencycode
///                   Body: { "currency_code": "USD", "from": "YYYY-MM-DD", "to": "YYYY-MM-DD" }
/// </summary>
public sealed class ForexClient
{
    private static readonly Uri TodayUri =
        new("https://api.nepalipatro.com.np/forex?web=true", UriKind.Absolute);

    private static readonly Uri HistoryUri =
        new("https://api.nepalipatro.com.np/forex/currencycode", UriKind.Absolute);

    private readonly HttpClient _httpClient;

    public ForexClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
    {
    }

    internal ForexClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0");
    }

    /// <summary>
    /// Fetches today's exchange rates for all currencies.
    /// </summary>
    public async Task<ForexTodayResponse?> FetchTodayAsync(CancellationToken cancellationToken = default)
    {
        var json = await _httpClient.GetStringAsync(TodayUri, cancellationToken);
        return JsonSerializer.Deserialize(json, ForexJsonContext.Default.ForexTodayResponse);
    }

    /// <summary>
    /// Fetches historical rates for a specific currency over a date range.
    /// Returns entries ordered oldest → newest.
    /// </summary>
    public async Task<List<ForexHistoryEntry>> FetchHistoryAsync(
        string currencyCode,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(
            new ForexHistoryRequest(
                currencyCode,
                // Invariant culture: the API needs Gregorian dates even on e.g. Thai/Arabic locales.
                from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ForexJsonContext.Default.ForexHistoryRequest);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(HistoryUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize(json, ForexJsonContext.Default.ListForexHistoryEntry)
               ?? new List<ForexHistoryEntry>();
    }
}
