using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingPositionDto(
    Guid Id,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal RemainingQuantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? Leverage,
    decimal UnrealizedPnL,
    DateTime OpenedAt,
    DateTime? UpdatedAt,
    PositionStatus Status
);
