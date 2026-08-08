using System;
using TradingBot.Application.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Mappers;

public static class ExchangePositionMapper
{
    public static Position ToDomain(ExchangePositionDto dto, Guid orderId)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.Symbol)) throw new ArgumentException("Symbol cannot be empty.", nameof(dto));
        if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(dto));
        if (dto.EntryPrice <= 0) throw new ArgumentException("Entry price must be greater than zero.", nameof(dto));

        var orderSide = dto.Side == PositionSide.Long ? OrderSide.Buy : OrderSide.Sell;

        var stopLoss = dto.StopLoss > 0 ? dto.StopLoss : null;
        var takeProfit = dto.TakeProfit > 0 ? dto.TakeProfit : null;
        var leverage = dto.Leverage > 0 ? dto.Leverage : null;
        var margin = dto.Margin >= 0 ? dto.Margin : null;

        var position = new Position(
            orderId: orderId,
            symbol: dto.Symbol,
            side: orderSide,
            entryPrice: dto.EntryPrice,
            quantity: dto.Quantity,
            stopLoss: stopLoss,
            takeProfit: takeProfit,
            exchangePositionId: dto.ExchangePositionId,
            leverage: leverage,
            margin: margin,
            fee: 0m,
            initialStatus: PositionStatus.Open
        );

        if (dto.MarkPrice > 0)
        {
            position.UpdatePrice(dto.MarkPrice);
        }

        return position;
    }
}
