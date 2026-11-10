using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenPatro.Models;

/// <summary>
/// Compile-time (source-generated) JSON metadata. Release builds are trimmed, and the
/// reflection-based serializer could lose model members the trimmer can't see; these
/// contexts make every (de)serialization path trim-safe.
/// Models carry explicit [JsonPropertyName] attributes, so default (case-sensitive) options apply.
/// </summary>
[JsonSerializable(typeof(BullionApiResponse))]
[JsonSerializable(typeof(RashifalResponse))]
[JsonSerializable(typeof(DateConvertRequest))]
[JsonSerializable(typeof(DateConvertResponse))]
[JsonSerializable(typeof(StockMarketHomeData))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}

/// <summary>The forex endpoints were always read case-insensitively; keep that behaviour.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ForexTodayResponse))]
[JsonSerializable(typeof(List<ForexHistoryEntry>))]
[JsonSerializable(typeof(ForexHistoryRequest))]
internal sealed partial class ForexJsonContext : JsonSerializerContext
{
}

/// <summary>Body for POST /forex/currencycode (was an anonymous object).</summary>
internal sealed record ForexHistoryRequest(
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To);
