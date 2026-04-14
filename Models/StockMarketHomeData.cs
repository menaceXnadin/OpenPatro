using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenPatro.Models;

public sealed class StockMarketHomeData
{
    [JsonPropertyName("marketStatus")]
    public MarketStatusInfo? MarketStatus { get; set; }

    [JsonPropertyName("marketSummary")]
    public List<MarketSummaryEntry>? MarketSummary { get; set; }

    [JsonPropertyName("stockSummary")]
    public StockSummaryInfo? StockSummary { get; set; }

    [JsonPropertyName("indices")]
    public List<MarketIndexInfo>? Indices { get; set; }

    [JsonPropertyName("subIndices")]
    public List<MarketIndexInfo>? SubIndices { get; set; }

    [JsonPropertyName("topGainers")]
    public List<MarketMoverInfo>? TopGainers { get; set; }

    [JsonPropertyName("topLosers")]
    public List<MarketMoverInfo>? TopLosers { get; set; }

    [JsonPropertyName("topTurnover")]
    public List<MarketMoverInfo>? TopTurnover { get; set; }

    [JsonPropertyName("topTradedShares")]
    public List<MarketMoverInfo>? TopTradedShares { get; set; }

    [JsonPropertyName("topTransactions")]
    public List<MarketMoverInfo>? TopTransactions { get; set; }

    [JsonPropertyName("liveCompanyData")]
    public List<LiveCompanyDataInfo>? LiveCompanyData { get; set; }

    [JsonPropertyName("demand")]
    public JsonElement Demand { get; set; }

    [JsonPropertyName("supply")]
    public JsonElement Supply { get; set; }
}

public sealed class MarketStatusInfo
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

public sealed class MarketSummaryEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public decimal Value { get; set; }
}

public sealed class StockSummaryInfo
{
    [JsonPropertyName("advanced")]
    public int Advanced { get; set; }

    [JsonPropertyName("declined")]
    public int Declined { get; set; }

    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    [JsonPropertyName("positiveCircuit")]
    public int PositiveCircuit { get; set; }

    [JsonPropertyName("negativeCircuit")]
    public int NegativeCircuit { get; set; }
}

public sealed class MarketIndexInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("currentValue")]
    public decimal CurrentValue { get; set; }

    [JsonPropertyName("change")]
    public decimal Change { get; set; }

    [JsonPropertyName("changePercent")]
    public decimal ChangePercent { get; set; }

    [JsonPropertyName("sector")]
    public string? Sector { get; set; }
}

public sealed class MarketMoverInfo
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("lastTradedPrice")]
    public decimal LastTradedPrice { get; set; }

    [JsonPropertyName("change")]
    public decimal Change { get; set; }

    [JsonPropertyName("changePercent")]
    public decimal ChangePercent { get; set; }

    [JsonPropertyName("turnover")]
    public decimal Turnover { get; set; }

    [JsonPropertyName("sharesTraded")]
    public decimal SharesTraded { get; set; }

    [JsonPropertyName("transactions")]
    public decimal Transactions { get; set; }

    [JsonPropertyName("companyLogo")]
    public string? CompanyLogo { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonIgnore]
    public string? LogoPath =>
        !string.IsNullOrWhiteSpace(CompanyLogo)
            ? CompanyLogo
            : Icon;
}

public sealed class LiveCompanyDataInfo
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("securityName")]
    public string SecurityName { get; set; } = string.Empty;

    [JsonPropertyName("sector")]
    public string? Sector { get; set; }

    [JsonPropertyName("lastTradedPrice")]
    public decimal LastTradedPrice { get; set; }

    [JsonPropertyName("change")]
    public decimal Change { get; set; }

    [JsonPropertyName("percentageChange")]
    public decimal PercentageChange { get; set; }

    [JsonPropertyName("totalTradeValue")]
    public decimal TotalTradeValue { get; set; }

    [JsonPropertyName("totalTradeQuantity")]
    public decimal TotalTradeQuantity { get; set; }

    [JsonPropertyName("totalTransactions")]
    public int TotalTransactions { get; set; }

    [JsonPropertyName("lastUpdatedDateTime")]
    public string? LastUpdatedDateTime { get; set; }

    [JsonPropertyName("openPrice")]
    public decimal OpenPrice { get; set; }

    [JsonPropertyName("highPrice")]
    public decimal HighPrice { get; set; }

    [JsonPropertyName("lowPrice")]
    public decimal LowPrice { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal PreviousClose { get; set; }

    [JsonPropertyName("companyLogo")]
    public string? CompanyLogo { get; set; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonIgnore]
    public string? LogoPath =>
        !string.IsNullOrWhiteSpace(CompanyLogo)
            ? CompanyLogo
            : !string.IsNullOrWhiteSpace(IconUrl)
                ? IconUrl
                : Icon;
}
