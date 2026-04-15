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
    /// Fetches bullion prices starting from the given AD date.
    /// The API returns all entries from that date up to today.
    /// </summary>
    /// <param name="fromDate">The start date (AD). Defaults to 30 days before today if not specified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BullionResponse?> FetchAsync(
        DateOnly? fromDate = null,
        CancellationToken cancellationToken = default)
    {
        // The original site uses a fixed from-date of 2026-3-16 (no zero-padding).
        // We replicate that format: YYYY-M-D
        var date = fromDate ?? GetDefaultFromDate();
        var url = $"{BullionsBaseUri}?from-date={date.Year}-{date.Month}-{date.Day}";

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
}
