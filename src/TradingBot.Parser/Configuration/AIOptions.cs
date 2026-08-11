namespace TradingBot.Parser.Configuration;

public class AIOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "Mock";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
}
