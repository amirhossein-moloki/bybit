using System.Collections.Generic;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Domain.SignalIntelligence.Models;

public class StructuredIntent
{
    public IntelligenceMessageType MessageType { get; set; } = IntelligenceMessageType.UNKNOWN;
    public TradingIntent Intent { get; set; } = TradingIntent.UNKNOWN;
    public IntentScope Scope { get; set; } = IntentScope.NONE;
    public List<string> Targets { get; set; } = new();
    public decimal Confidence { get; set; }
    public bool RequiresExecution { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DetectionMethod { get; set; } = "RULE"; // "RULE" or "AI"

    // AI Understanding Extended Fields
    public ResolutionStatus ResolutionStatus { get; set; } = ResolutionStatus.NotTradingRelated;
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.NoAction;
    public bool IsTradeRelated { get; set; }
    public bool IsExecutableCandidate { get; set; }
    public string? Action { get; set; }
    public string? Symbol { get; set; }
    public string? PositionReference { get; set; }
    public string? Side { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit1 { get; set; }
    public decimal? TakeProfit2 { get; set; }
    public decimal? TakeProfit3 { get; set; }
    public List<string> Conditions { get; set; } = new();
    public List<string> Evidence { get; set; } = new();
}
