using System;
using System.Collections.Generic;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Services;

public class StreakCalculator
{
    public StreakMetricsDto Calculate(IReadOnlyList<decimal> netPnList)
    {
        if (netPnList == null || netPnList.Count == 0)
        {
            return new StreakMetricsDto(
                CurrentWinStreak: 0,
                CurrentLossStreak: 0,
                MaximumWinStreak: 0,
                MaximumLossStreak: 0
            );
        }

        int currentWinStreak = 0;
        int currentLossStreak = 0;
        int maxWinStreak = 0;
        int maxLossStreak = 0;

        foreach (var netPnL in netPnList)
        {
            if (netPnL > 0)
            {
                currentWinStreak++;
                currentLossStreak = 0;
                if (currentWinStreak > maxWinStreak)
                {
                    maxWinStreak = currentWinStreak;
                }
            }
            else if (netPnL < 0)
            {
                currentLossStreak++;
                currentWinStreak = 0;
                if (currentLossStreak > maxLossStreak)
                {
                    maxLossStreak = currentLossStreak;
                }
            }
            else // netPnL == 0 (Breakeven resets both)
            {
                currentWinStreak = 0;
                currentLossStreak = 0;
            }
        }

        return new StreakMetricsDto(
            CurrentWinStreak: currentWinStreak,
            CurrentLossStreak: currentLossStreak,
            MaximumWinStreak: maxWinStreak,
            MaximumLossStreak: maxLossStreak
        );
    }
}
