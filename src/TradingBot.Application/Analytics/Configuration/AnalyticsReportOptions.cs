namespace TradingBot.Application.Analytics.Configuration;

public class AnalyticsReportOptions
{
    public bool EnableCaching { get; set; } = true;
    public int CacheTtlMinutes { get; set; } = 5;
    public decimal DefaultInitialBalance { get; set; } = 10000m;
}
