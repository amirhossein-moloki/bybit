using System;
using System.Collections.Generic;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Services;

public class DrawdownCalculator
{
    public DrawdownMetricsDto Calculate(IReadOnlyList<decimal> netPnList, decimal initialBalance)
    {
        if (netPnList == null || netPnList.Count == 0)
        {
            return new DrawdownMetricsDto(
                PeakEquity: initialBalance,
                CurrentEquity: initialBalance,
                Drawdown: 0m,
                MaximumDrawdown: 0m,
                MaximumDrawdownPercentage: 0m
            );
        }

        decimal currentEquity = initialBalance;
        decimal peakEquity = initialBalance;
        decimal maxDrawdown = 0m;
        decimal maxDrawdownPercent = 0m;

        foreach (var netPnL in netPnList)
        {
            currentEquity += netPnL;
            if (currentEquity > peakEquity)
            {
                peakEquity = currentEquity;
            }

            decimal drawdown = peakEquity - currentEquity;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }

            decimal drawdownPercent = peakEquity > 0 ? (drawdown / peakEquity) * 100m : 0m;
            if (drawdownPercent > maxDrawdownPercent)
            {
                maxDrawdownPercent = drawdownPercent;
            }
        }

        decimal currentDrawdown = peakEquity - currentEquity;

        return new DrawdownMetricsDto(
            PeakEquity: peakEquity,
            CurrentEquity: currentEquity,
            Drawdown: currentDrawdown,
            MaximumDrawdown: maxDrawdown,
            MaximumDrawdownPercentage: maxDrawdownPercent
        );
    }
}
