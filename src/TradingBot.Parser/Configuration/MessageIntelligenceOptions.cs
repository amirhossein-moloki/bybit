namespace TradingBot.Parser.Configuration;

public class MessageIntelligenceOptions
{
    public bool Enabled { get; set; } = true;
    public bool ShadowMode { get; set; } = false;
    public decimal MinimumConfidence { get; set; } = 0.85m;
    public decimal MediumConfidenceThreshold { get; set; } = 0.60m;
}
