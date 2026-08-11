using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record StreakMetricsDto(
    int CurrentWinStreak,
    int CurrentLossStreak,
    int MaximumWinStreak,
    int MaximumLossStreak
);
