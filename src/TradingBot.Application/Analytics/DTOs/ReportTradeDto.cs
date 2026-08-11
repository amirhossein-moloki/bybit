using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record ReportTradeDto(
    Guid Id,
    Guid? PositionId,
    string Symbol,
    OrderSide Side,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal Quantity,
    decimal? ProfitLoss, // Gross PnL
    decimal Fee,
    decimal? FundingFee,
    decimal NetPnL,
    CloseReason? CloseReason,
    DateTime? OpenedAt,
    DateTime? ClosedAt
);
