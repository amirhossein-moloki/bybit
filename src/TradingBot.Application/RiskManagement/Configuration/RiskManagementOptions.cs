namespace TradingBot.Application.RiskManagement.Configuration;

public class RiskManagementOptions
{
    public const string SectionName = "RiskManagement";

    public bool Enabled { get; set; } = true;
    public string DefaultProfile { get; set; } = "Balanced";

    public bool RejectOnCritical { get; set; } = true;
    public bool AutoReduceLeverage { get; set; } = false;
    public bool OnePositionPerSymbol { get; set; } = true;
    public decimal MinimumRiskReward { get; set; } = 1.5m;
    public decimal MaximumExposure { get; set; } = 40.0m; // as a percentage of account balance, e.g., 40.0%
    public decimal MaximumDrawdown { get; set; } = 20.0m; // as a percentage of account balance, e.g., 20.0%
    public decimal MaximumDailyLoss { get; set; } = 5.0m; // as a percentage of account balance, e.g., 5.0%
    public decimal MaxRiskPerTrade { get; set; } = 1.0m;   // as a percentage of account balance, e.g., 1.0%
    public int MaxOpenPositions { get; set; } = 5;
    public int MaximumLeverage { get; set; } = 10;
}
