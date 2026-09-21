using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TradingBot.Parser.Models;

public class AIUnderstandingResult
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("is_trade_related")]
    public bool IsTradeRelated { get; set; }

    [JsonPropertyName("is_executable_candidate")]
    public bool IsExecutableCandidate { get; set; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("position_reference")]
    public string? PositionReference { get; set; }

    [JsonPropertyName("side")]
    public string? Side { get; set; }

    [JsonPropertyName("entry_price")]
    public decimal? EntryPrice { get; set; }

    [JsonPropertyName("stop_loss")]
    public decimal? StopLoss { get; set; }

    [JsonPropertyName("take_profit_1")]
    public decimal? TakeProfit1 { get; set; }

    [JsonPropertyName("take_profit_2")]
    public decimal? TakeProfit2 { get; set; }

    [JsonPropertyName("take_profit_3")]
    public decimal? TakeProfit3 { get; set; }

    [JsonPropertyName("conditions")]
    public List<string> Conditions { get; set; } = new();

    [JsonPropertyName("execution_mode")]
    public string ExecutionMode { get; set; } = "NoAction";

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; set; }

    [JsonPropertyName("resolution_status")]
    public string ResolutionStatus { get; set; } = "NotTradingRelated";

    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = new();

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Backward compatibility helpers
    [JsonPropertyName("type")]
    public string Type
    {
        get => string.IsNullOrEmpty(Intent) ? "UNKNOWN" : Intent;
        set { if (string.IsNullOrEmpty(Intent)) Intent = value; }
    }

    [JsonPropertyName("entry")]
    public decimal? Entry
    {
        get => EntryPrice;
        set { if (!EntryPrice.HasValue) EntryPrice = value; }
    }

    [JsonPropertyName("take_profit")]
    public List<decimal> TakeProfits
    {
        get
        {
            var list = new List<decimal>();
            if (TakeProfit1.HasValue) list.Add(TakeProfit1.Value);
            if (TakeProfit2.HasValue) list.Add(TakeProfit2.Value);
            if (TakeProfit3.HasValue) list.Add(TakeProfit3.Value);
            return list;
        }
        set
        {
            if (value != null)
            {
                if (value.Count > 0) TakeProfit1 = value[0];
                if (value.Count > 1) TakeProfit2 = value[1];
                if (value.Count > 2) TakeProfit3 = value[2];
            }
        }
    }
}
