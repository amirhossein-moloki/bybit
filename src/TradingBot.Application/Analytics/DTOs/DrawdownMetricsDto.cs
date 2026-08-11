using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record DrawdownMetricsDto(
    decimal PeakEquity,
    decimal CurrentEquity,
    decimal Drawdown,
    decimal MaximumDrawdown,
    decimal MaximumDrawdownPercentage
);
