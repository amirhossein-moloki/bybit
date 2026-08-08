using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TradingBot.Exchange.Bybit.Dtos;

public class BybitPositionListResponse
{
    [JsonPropertyName("list")]
    public List<BybitPositionInfo> List { get; set; } = new();

    [JsonPropertyName("nextPageCursor")]
    public string NextPageCursor { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
}

public class BybitPositionInfo
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;

    [JsonPropertyName("avgPrice")]
    public string AvgPrice { get; set; } = string.Empty;

    [JsonPropertyName("markPrice")]
    public string MarkPrice { get; set; } = string.Empty;

    [JsonPropertyName("leverage")]
    public string Leverage { get; set; } = string.Empty;

    [JsonPropertyName("positionBalance")]
    public string PositionBalance { get; set; } = string.Empty;

    [JsonPropertyName("liqPrice")]
    public string LiqPrice { get; set; } = string.Empty;

    [JsonPropertyName("unrealisedPnl")]
    public string UnrealisedPnl { get; set; } = string.Empty;

    [JsonPropertyName("takeProfit")]
    public string TakeProfit { get; set; } = string.Empty;

    [JsonPropertyName("stopLoss")]
    public string StopLoss { get; set; } = string.Empty;

    [JsonPropertyName("createdTime")]
    public string CreatedTime { get; set; } = string.Empty;

    [JsonPropertyName("updatedTime")]
    public string UpdatedTime { get; set; } = string.Empty;
}
