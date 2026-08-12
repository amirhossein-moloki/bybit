namespace TradingBot.Parser.Models;

public class AIProcessingDecision
{
    public bool ShouldUseAI { get; set; }
    public string Reason { get; set; } = string.Empty;
}
