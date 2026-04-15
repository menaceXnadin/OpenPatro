using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenPatro.Models;

/// <summary>
/// A single currency entry from the today's forex endpoint.
/// </summary>
public sealed class ForexRateEntry
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    [JsonPropertyName("buying")]
    public decimal Buying { get; init; }

    [JsonPropertyName("selling")]
    public decimal Selling { get; init; }

    [JsonPropertyName("unit")]
    public int Unit { get; init; } = 1;

    /// <summary>"fixed" for INR, "open" for all others.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;
}

/// <summary>
/// Top-level response from GET /forex?web=true
/// </summary>
public sealed class ForexTodayResponse
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public List<ForexRateEntry>? Data { get; init; }
}

/// <summary>
/// A single entry from the currency history endpoint (POST /forex/currencycode).
/// </summary>
public sealed class ForexHistoryEntry
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    [JsonPropertyName("buying")]
    public decimal Buying { get; init; }

    [JsonPropertyName("selling")]
    public decimal Selling { get; init; }

    [JsonPropertyName("unit")]
    public int Unit { get; init; } = 1;

    [JsonPropertyName("added_date")]
    public string AddedDate { get; init; } = string.Empty;
}
