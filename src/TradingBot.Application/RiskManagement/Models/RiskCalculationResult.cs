namespace TradingBot.Application.RiskManagement.Models;

public class RiskCalculationResult
{
    public decimal RiskAmount { get; set; }

    public decimal PositionSize { get; set; }

    public decimal StopLossDistance { get; set; }

    public decimal RiskReward { get; set; }

    public decimal RequiredMargin { get; set; }
}
