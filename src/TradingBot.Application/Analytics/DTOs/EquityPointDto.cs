using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record EquityPointDto(
    int TradeIndex,
    Guid? TradeId,
    DateTime ClosedAt,
    decimal NetPnL,
    decimal CumulativePnL,
    decimal Equity,
    decimal Drawdown,
    decimal DrawdownPercentage,
    decimal PeakEquity
);
