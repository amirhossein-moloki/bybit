namespace TradingBot.Infrastructure.RiskManagement.Configuration;

public class RiskManagementOptions
{
    public const string SectionName = "RiskManagement";

    public bool Enabled { get; set; } = true;
    public string DefaultProfile { get; set; } = "Balanced";
    public decimal MaxRiskPerTrade { get; set; } = 1.0m;
    public decimal MaximumLeverage { get; set; } = 10.0m;
}
