using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Models;

public record ExchangePositionDto(
    string? ExchangePositionId,
    string Symbol,
    PositionSide Side,
    decimal Quantity,
    decimal EntryPrice,
    decimal MarkPrice,
    decimal? Leverage,
    decimal? Margin,
    decimal UnrealizedPnL,
    decimal? LiquidationPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    DateTime? UpdatedAt
);
