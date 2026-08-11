using System.Collections.Generic;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Parser.Models;

public class SignalExtractionResult
{
    public bool Success { get; set; }
    public string? Symbol { get; set; }
    public TradeSide Side { get; set; } = TradeSide.UNKNOWN;
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public List<TakeProfitTarget> TakeProfits { get; set; } = new();
    public decimal? Leverage { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public decimal Confidence { get; set; }
    public ExtractionValidationStatus Status { get; set; } = ExtractionValidationStatus.Invalid;
}
