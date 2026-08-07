namespace TradingBot.Application.RiskManagement.Configuration;

public class RiskCalculationOptions
{
    public decimal DefaultRiskPercent { get; set; } = 1.0m;
    public int RoundingPrecision { get; set; } = 8;
}
