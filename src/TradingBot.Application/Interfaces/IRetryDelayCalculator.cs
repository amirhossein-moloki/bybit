using System;
using TradingBot.Application.Configuration;

namespace TradingBot.Application.Interfaces;

public interface IRetryDelayCalculator
{
    TimeSpan CalculateDelay(int attempt, ReliabilityOptions options);
}
