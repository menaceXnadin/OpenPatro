using System.Text.Json.Serialization;

namespace OpenPatro.Models;

/// <summary>
/// Request body for POST https://api.nepalipatro.com.np/calendars/dateConvert
/// </summary>
public sealed class DateConvertRequest
{
    /// <summary>Date in YYYY-MM-DD format (zero-padded month and day).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>"BS" for BS→AD conversion, "AD" for AD→BS conversion.</summary>
    [JsonPropertyName("based_on")]
    public string BasedOn { get; set; } = string.Empty;
}

/// <summary>
/// Response from the date conversion endpoint.
/// Always contains both AD and BS dates regardless of conversion direction.
/// </summary>
public sealed class DateConvertResponse
{
    /// <summary>AD date in YYYY-MM-DD format.</summary>
    [JsonPropertyName("ad")]
    public string Ad { get; set; } = string.Empty;

    /// <summary>BS date in DD.MM.YYYY format.</summary>
    [JsonPropertyName("bs")]
    public string Bs { get; set; } = string.Empty;

    [JsonPropertyName("bs_day")]
    public int BsDay { get; set; }

    [JsonPropertyName("bs_month")]
    public int BsMonth { get; set; }

    [JsonPropertyName("bs_year")]
    public int BsYear { get; set; }

    /// <summary>
    /// Tithi number. 1–15 = Shukla Paksha, 16–29 = Krishna Paksha, 30 = Aunsi.
    /// </summary>
    [JsonPropertyName("tithi")]
    public int Tithi { get; set; }

    /// <summary>Nepal Sambat year.</summary>
    [JsonPropertyName("ns_year")]
    public int NsYear { get; set; }

    /// <summary>Lunar month number.</summary>
    [JsonPropertyName("chandrama")]
    public int Chandrama { get; set; }

    [JsonPropertyName("is_verified")]
    public int IsVerified { get; set; }

    /// <summary>
    /// Returns human-readable Tithi/Paksha label.
    /// </summary>
    public string TithiLabel
    {
        get
        {
            if (Tithi == 30) return "Aunsi (अमावस्या)";
            if (Tithi is >= 1 and <= 15) return $"Shukla Paksha — Tithi {Tithi}";
            if (Tithi is >= 16 and <= 29) return $"Krishna Paksha — Tithi {Tithi - 15}";
            return $"Tithi {Tithi}";
        }
    }
}
