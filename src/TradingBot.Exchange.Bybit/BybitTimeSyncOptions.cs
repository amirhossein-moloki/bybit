namespace TradingBot.Exchange.Bybit;

public class BybitTimeSyncOptions
{
    public bool Enabled { get; set; } = true;
    public int RefreshIntervalMinutes { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 5;
}
