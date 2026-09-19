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
}
