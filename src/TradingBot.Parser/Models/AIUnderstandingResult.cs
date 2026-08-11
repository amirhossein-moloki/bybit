using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TradingBot.Parser.Models;

public class AIUnderstandingResult
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("entry")]
    public decimal? Entry { get; set; }

    [JsonPropertyName("stop_loss")]
    public decimal? StopLoss { get; set; }

    [JsonPropertyName("take_profit")]
    public List<decimal> TakeProfits { get; set; } = new();

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
