using System;
using System.Collections.Generic;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record PerformanceReportDto(
    DateTime GeneratedAt,
    DateTime? StartDate,
    DateTime? EndDate,
    decimal InitialBalance,
    decimal FinalBalance,
    PerformanceMetricsDto Metrics,
    DrawdownMetricsDto Drawdown,
    StreakMetricsDto Streaks,
    DurationMetricsDto Durations,
    LongShortPerformanceDto LongShort,
    IReadOnlyList<EquityPointDto> EquityCurve,
    IReadOnlyList<ReportTradeDto> DetailedTrades
);
