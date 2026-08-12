namespace TradingBot.Application.SignalIntelligence.Configuration;

public class SignalIntelligenceOptions
{
    public const string SectionName = "SignalIntelligence";

    public decimal MinimumConfidence { get; set; } = 0.85m;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelay { get; set; } = 1000; // in milliseconds
    public string BackoffStrategy { get; set; } = "Exponential"; // "Exponential" or "Linear"
}
