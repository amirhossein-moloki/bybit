using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingTradeDto(
    Guid Id,
    Guid? PositionId,
    string Symbol,
    OrderSide Side,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal Quantity,
    decimal GrossPnL,
    decimal Fee,
    decimal NetPnL,
    CloseReason? CloseReason,
    DateTime? OpenedAt,
    DateTime ClosedAt
);
