using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenPatro.Models;

/// <summary>
/// Represents a single day's bullion price entry from the NepaliPatro bullions API.
/// </summary>
public sealed class BullionDayEntry
{
    /// <summary>Bikram Sambat date string, e.g. "02.01.2083"</summary>
    [JsonPropertyName("bs")]
    public string Bs { get; init; } = string.Empty;

    /// <summary>Hallmark gold price per 10 gram (in NPR)</summary>
    [JsonPropertyName("g_ha")]
    public decimal GoldHallmarkPer10g { get; init; }

    /// <summary>Tejabi gold price per 10 gram (in NPR)</summary>
    [JsonPropertyName("g_te")]
    public decimal GoldTejabiPer10g { get; init; }

    /// <summary>Silver price per 10 gram (in NPR)</summary>
    [JsonPropertyName("g_s")]
    public decimal SilverPer10g { get; init; }

    /// <summary>Hallmark gold price per tola (in NPR)</summary>
    [JsonPropertyName("t_ha")]
    public decimal GoldHallmarkPerTola { get; init; }

    /// <summary>Tejabi gold price per tola (in NPR)</summary>
    [JsonPropertyName("t_te")]
    public decimal GoldTejabiPerTola { get; init; }

    /// <summary>Silver price per tola (in NPR)</summary>
    [JsonPropertyName("t_s")]
    public decimal SilverPerTola { get; init; }
}

/// <summary>
/// Top-level response from the bullions API.
/// Shape: { "source": "...", "data": { "2026-04-15": { ... }, ... } }
/// </summary>
public sealed class BullionApiResponse
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, BullionDayEntry>? Data { get; init; }
}

/// <summary>
/// Parsed bullion response with entries keyed by AD date string (YYYY-MM-DD).
/// </summary>
public sealed class BullionResponse
{
    /// <summary>Entries keyed by AD date, e.g. "2026-04-15"</summary>
    public Dictionary<string, BullionDayEntry> Entries { get; init; } = new();
}
