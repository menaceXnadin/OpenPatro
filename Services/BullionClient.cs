using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenPatro.Models;

namespace OpenPatro.Services;

/// <summary>
/// HTTP client for the NepaliPatro bullions API.
/// Endpoint: GET https://api.nepalipatro.com.np/v3/bullions?from-date=YYYY-M-D
/// The API returns a flat JSON object keyed by AD date strings.
/// </summary>
public sealed class BullionClient
{
    private static readonly Uri BullionsBaseUri = new("https://api.nepalipatro.com.np/v3/bullions", UriKind.Absolute);

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public BullionClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenPatro/1.0");
    }

    /// <summary>
    /// Fetches bullion prices from the API.
    /// </summary>
    /// <param name="fromDate">Optional start date (AD). Ignored when <paramref name="includeAllHistory"/> is true.</param>
    /// <param name="includeAllHistory">When true, omits the from-date query and requests all available history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BullionResponse?> FetchAsync(
        DateOnly? fromDate = null,
        bool includeAllHistory = false,
        CancellationToken cancellationToken = default)
    {
        var url = includeAllHistory
            ? BullionsBaseUri.ToString()
            : BuildFromDateUrl(fromDate ?? GetDefaultFromDate());

        var json = await _httpClient.GetStringAsync(url, cancellationToken);

        // The API returns: { "source": "Fenegosida", "data": { "2026-04-15": { ... }, ... } }
        var apiResponse = JsonSerializer.Deserialize<BullionApiResponse>(json);
        var entries = apiResponse?.Data ?? new Dictionary<string, BullionDayEntry>();

        return new BullionResponse { Entries = entries };
    }

    /// <summary>
    /// Returns a from-date 30 days before today, so the table always shows
    /// a rolling ~30-day window regardless of when the app is run.
    /// </summary>
    private static DateOnly GetDefaultFromDate()
    {
        return DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    }

    private static string BuildFromDateUrl(DateOnly date)
    {
        // API expects YYYY-M-D (without zero-padding).
        return $"{BullionsBaseUri}?from-date={date.Year}-{date.Month}-{date.Day}";
    }
}
