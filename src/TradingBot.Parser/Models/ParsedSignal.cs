using System.Collections.Generic;
using TradingBot.Domain.Enums;

namespace TradingBot.Parser.Models;

public class ParsedSignal
{
    public string? Symbol { get; set; }
    public OrderSide? Side { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public List<decimal> TakeProfits { get; set; } = new();
    public int? Leverage { get; set; }
    public double? ConfidenceScore { get; set; }

    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
}
