using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record AnalyticsTradeDto(
    Guid Id,
    decimal? NetPnL,
    decimal? ProfitLoss,
    decimal Fee,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    string Symbol,
    OrderSide Side
);
