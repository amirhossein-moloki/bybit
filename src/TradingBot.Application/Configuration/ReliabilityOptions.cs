using System;

namespace TradingBot.Application.Configuration;

public class ReliabilityOptions
{
    public RetrySettings Retry { get; set; } = new();
    public TimeoutSettings Timeout { get; set; } = new();
    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();

    public void Validate()
    {
        if (Retry == null) throw new ArgumentNullException(nameof(Retry));
        if (Timeout == null) throw new ArgumentNullException(nameof(Timeout));
        if (CircuitBreaker == null) throw new ArgumentNullException(nameof(CircuitBreaker));

        if (Retry.Enabled)
        {
            if (Retry.MaxAttempts < 0)
                throw new ArgumentException("MaxAttempts must be non-negative.");
            if (Retry.InitialDelaySeconds < 0)
                throw new ArgumentException("InitialDelay must be non-negative.");
            if (Retry.MaxDelaySeconds < Retry.InitialDelaySeconds)
                throw new ArgumentException("MaxDelay must be greater than or equal to InitialDelay.");
            if (Retry.BackoffMultiplier <= 0)
                throw new ArgumentException("BackoffMultiplier must be greater than zero.");
        }

        if (Timeout.Enabled)
        {
            if (Timeout.DefaultTimeoutSeconds <= 0)
                throw new ArgumentException("DefaultTimeout must be greater than zero.");
        }

        if (CircuitBreaker.Enabled)
        {
            if (CircuitBreaker.FailureThreshold <= 0)
                throw new ArgumentException("FailureThreshold must be greater than zero.");
            if (CircuitBreaker.BreakDurationSeconds <= 0)
                throw new ArgumentException("BreakDuration must be greater than zero.");
            if (CircuitBreaker.HalfOpenProbeCount <= 0)
                throw new ArgumentException("HalfOpenProbeCount must be greater than zero.");
        }
    }
}

public class RetrySettings
{
    public bool Enabled { get; set; } = true;
    public int MaxAttempts { get; set; } = 3;
    public double InitialDelaySeconds { get; set; } = 1.0;
    public double MaxDelaySeconds { get; set; } = 10.0;
    public double BackoffMultiplier { get; set; } = 2.0;
    public bool JitterEnabled { get; set; } = true;

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(InitialDelaySeconds);
    public TimeSpan MaxDelay => TimeSpan.FromSeconds(MaxDelaySeconds);
}

public class TimeoutSettings
{
    public bool Enabled { get; set; } = true;
    public double DefaultTimeoutSeconds { get; set; } = 15.0;

    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(DefaultTimeoutSeconds);
}

public class CircuitBreakerSettings
{
    public bool Enabled { get; set; } = true;
    public int FailureThreshold { get; set; } = 5;
    public double BreakDurationSeconds { get; set; } = 30.0;
    public int HalfOpenProbeCount { get; set; } = 3;

    public TimeSpan BreakDuration => TimeSpan.FromSeconds(BreakDurationSeconds);
}
