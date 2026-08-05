using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class RiskRule
{
    public Guid Id { get; private set; }
    public decimal MaxRiskPercent { get; private set; }
    public int MaxOpenPositions { get; private set; }
    public decimal MaxDailyLoss { get; private set; }
    public int MaxLeverage { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private RiskRule()
    {
        Id = Guid.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public RiskRule(decimal maxRiskPercent, int maxOpenPositions, decimal maxDailyLoss, int maxLeverage)
    {
        if (maxRiskPercent < 0 || maxRiskPercent > 100)
        {
            throw new DomainException("MaxRiskPercent must be between 0 and 100.");
        }

        if (maxOpenPositions <= 0)
        {
            throw new DomainException("MaxOpenPositions must be greater than zero.");
        }

        if (maxDailyLoss < 0)
        {
            throw new DomainException("MaxDailyLoss cannot be negative.");
        }

        if (maxLeverage < 1)
        {
            throw new DomainException("MaxLeverage must be at least 1.");
        }

        Id = Guid.NewGuid();
        MaxRiskPercent = maxRiskPercent;
        MaxOpenPositions = maxOpenPositions;
        MaxDailyLoss = maxDailyLoss;
        MaxLeverage = maxLeverage;
        CreatedAt = DateTime.UtcNow;
    }
}
