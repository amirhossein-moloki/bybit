using System;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;

namespace TradingBot.Application.Services;

public class RetryDelayCalculator : IRetryDelayCalculator
{
    private readonly Random _random;

    public RetryDelayCalculator()
    {
        _random = new Random();
    }

    public RetryDelayCalculator(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public TimeSpan CalculateDelay(int attempt, ReliabilityOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (attempt < 1) attempt = 1;

        var retrySettings = options.Retry;

        // Base formula: Delay = InitialDelay × BackoffMultiplier^(Attempt - 1)
        double baseSeconds = retrySettings.InitialDelaySeconds * Math.Pow(retrySettings.BackoffMultiplier, attempt - 1);

        if (retrySettings.JitterEnabled)
        {
            // Bounded jitter: +/- 20% of the calculated delay
            // random value between 0.8 and 1.2
            double jitterFactor = 0.8 + (_random.NextDouble() * 0.4);
            baseSeconds *= jitterFactor;
        }

        // Must never exceed MaxDelay
        if (baseSeconds > retrySettings.MaxDelaySeconds)
        {
            baseSeconds = retrySettings.MaxDelaySeconds;
        }

        // Must never be negative or zero
        if (baseSeconds <= 0)
        {
            baseSeconds = 0.001; // 1 ms minimum
        }

        return TimeSpan.FromSeconds(baseSeconds);
    }
}
