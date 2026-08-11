using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record DurationMetricsDto(
    TimeSpan? AverageDuration,
    TimeSpan? ShortestDuration,
    TimeSpan? LongestDuration,
    TimeSpan? AverageWinningDuration,
    TimeSpan? AverageLosingDuration
);
