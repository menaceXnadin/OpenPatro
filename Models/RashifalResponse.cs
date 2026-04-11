using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenPatro.Models;

/// <summary>
/// Top-level response from GET https://nepalipatro.com.np/rashifal/getv5/type/dwmy
/// </summary>
public sealed class RashifalResponse
{
    [JsonPropertyName("np")]
    public List<RashifalEntry> Np { get; set; } = new();
}

/// <summary>
/// One of the 4 rashifal entries (D=daily, W=weekly, M=monthly, Y=yearly).
/// Each contains 12 zodiac sign predictions in Nepali.
/// </summary>
public sealed class RashifalEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("todate")]
    public string ToDate { get; set; } = string.Empty;

    [JsonPropertyName("aries")]
    public string Aries { get; set; } = string.Empty;

    [JsonPropertyName("taurus")]
    public string Taurus { get; set; } = string.Empty;

    [JsonPropertyName("gemini")]
    public string Gemini { get; set; } = string.Empty;

    [JsonPropertyName("cancer")]
    public string Cancer { get; set; } = string.Empty;

    [JsonPropertyName("leo")]
    public string Leo { get; set; } = string.Empty;

    [JsonPropertyName("virgo")]
    public string Virgo { get; set; } = string.Empty;

    [JsonPropertyName("libra")]
    public string Libra { get; set; } = string.Empty;

    [JsonPropertyName("scorpio")]
    public string Scorpio { get; set; } = string.Empty;

    [JsonPropertyName("sagittarius")]
    public string Sagittarius { get; set; } = string.Empty;

    [JsonPropertyName("capricorn")]
    public string Capricorn { get; set; } = string.Empty;

    [JsonPropertyName("aquarius")]
    public string Aquarius { get; set; } = string.Empty;

    [JsonPropertyName("pisces")]
    public string Pisces { get; set; } = string.Empty;

    /// <summary>
    /// Returns the prediction text for a given zodiac key (e.g. "aries", "taurus").
    /// </summary>
    public string GetPrediction(string zodiacKey)
    {
        return zodiacKey.ToLowerInvariant() switch
        {
            "aries" => Aries,
            "taurus" => Taurus,
            "gemini" => Gemini,
            "cancer" => Cancer,
            "leo" => Leo,
            "virgo" => Virgo,
            "libra" => Libra,
            "scorpio" => Scorpio,
            "sagittarius" => Sagittarius,
            "capricorn" => Capricorn,
            "aquarius" => Aquarius,
            "pisces" => Pisces,
            _ => string.Empty
        };
    }

    /// <summary>
    /// The canonical ordered list of all 12 zodiac sign keys.
    /// </summary>
    public static readonly string[] ZodiacKeys =
    {
        "aries", "taurus", "gemini", "cancer", "leo", "virgo",
        "libra", "scorpio", "sagittarius", "capricorn", "aquarius", "pisces"
    };
}
