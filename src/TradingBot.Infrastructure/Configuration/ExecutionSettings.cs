namespace TradingBot.Infrastructure.Configuration;

public class ExecutionSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxConcurrentExecutions { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 30;
}
