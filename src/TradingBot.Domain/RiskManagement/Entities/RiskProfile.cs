using System;

namespace TradingBot.Domain.RiskManagement.Entities;

public class RiskProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Balanced";
    public decimal MaxRiskPerTrade { get; set; } = 1.0m;
    public decimal MaxDailyLoss { get; set; }
    public decimal MaxWeeklyLoss { get; set; }
    public decimal MaxMonthlyLoss { get; set; }
    public int MaxOpenPositions { get; set; } = 5;
    public int MaxLeverage { get; set; } = 10;
    public decimal MaxExposure { get; set; }
    public decimal MinimumRiskReward { get; set; } = 2.0m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public RiskProfile()
    {
        Id = Guid.NewGuid();
    }
}
