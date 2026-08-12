using System;

namespace TradingBot.Application.Exceptions;

public class RateLimitException : Exception
{
    public TimeSpan RetryAfter { get; }

    public RateLimitException(string message, TimeSpan retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
    }
}
