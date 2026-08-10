using System;

namespace TradingBot.Application.Configuration;

public class IdempotencyOptions
{
    public bool Enabled { get; set; } = true;
    public double IncompleteOperationTimeoutSeconds { get; set; } = 60.0;
    public double RecoveryIntervalSeconds { get; set; } = 30.0;
    public double EventRetentionDays { get; set; } = 7.0;

    public TimeSpan IncompleteOperationTimeout => TimeSpan.FromSeconds(IncompleteOperationTimeoutSeconds);
    public TimeSpan RecoveryInterval => TimeSpan.FromSeconds(RecoveryIntervalSeconds);
}
