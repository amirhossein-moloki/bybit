using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Position
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string Symbol { get; private set; }
    public OrderSide Side { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? TakeProfit { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public decimal UnrealizedPnL { get; private set; }
    public PositionStatus Status { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    // Required for EF Core
    private Position()
    {
        Id = Guid.Empty;
        OrderId = Guid.Empty;
        Symbol = string.Empty;
        Side = OrderSide.Buy;
        Status = PositionStatus.Closed;
        OpenedAt = DateTime.UtcNow;
    }

    public Position(Guid orderId, string symbol, OrderSide side, decimal entryPrice, decimal quantity, decimal? stopLoss = null, decimal? takeProfit = null)
    {
        if (orderId == Guid.Empty)
        {
            throw new DomainException("OrderId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("Symbol cannot be empty.");
        }

        if (entryPrice <= 0)
        {
            throw new DomainException("EntryPrice must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        if (stopLoss.HasValue && stopLoss.Value <= 0)
        {
            throw new DomainException("StopLoss price must be greater than zero.");
        }

        if (takeProfit.HasValue && takeProfit.Value <= 0)
        {
            throw new DomainException("TakeProfit price must be greater than zero.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        Symbol = symbol.Trim().ToUpperInvariant();
        Side = side;
        EntryPrice = entryPrice;
        Quantity = quantity;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        CurrentPrice = entryPrice;
        UnrealizedPnL = 0m;
        Status = PositionStatus.Open;
        OpenedAt = DateTime.UtcNow;
    }

    public void UpdatePrice(decimal currentPrice)
    {
        if (Status != PositionStatus.Open)
        {
            throw new DomainException("Cannot update price of a closed or liquidated position.");
        }

        if (currentPrice <= 0)
        {
            throw new DomainException("Current price must be greater than zero.");
        }

        CurrentPrice = currentPrice;

        if (Side == OrderSide.Buy)
        {
            UnrealizedPnL = (CurrentPrice - EntryPrice) * Quantity;
        }
        else
        {
            UnrealizedPnL = (EntryPrice - CurrentPrice) * Quantity;
        }
    }

    public void Close(decimal exitPrice)
    {
        if (Status != PositionStatus.Open)
        {
            throw new DomainException($"Invalid transition: Cannot close a position that is already in {Status} state.");
        }

        if (exitPrice <= 0)
        {
            throw new DomainException("Exit price must be greater than zero.");
        }

        CurrentPrice = exitPrice;
        if (Side == OrderSide.Buy)
        {
            UnrealizedPnL = (CurrentPrice - EntryPrice) * Quantity;
        }
        else
        {
            UnrealizedPnL = (EntryPrice - CurrentPrice) * Quantity;
        }

        Status = PositionStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    public void Liquidate()
    {
        if (Status != PositionStatus.Open)
        {
            throw new DomainException($"Invalid transition: Cannot liquidate a position that is in {Status} state.");
        }

        if (Side == OrderSide.Buy)
        {
            UnrealizedPnL = -EntryPrice * Quantity; // Total loss
        }
        else
        {
            UnrealizedPnL = -EntryPrice * Quantity; // Total loss
        }

        CurrentPrice = 0m;
        Status = PositionStatus.Liquidated;
        ClosedAt = DateTime.UtcNow;
    }

    public void UpdateRiskRules(decimal? stopLoss, decimal? takeProfit)
    {
        if (Status != PositionStatus.Open)
        {
            throw new DomainException("Cannot update risk rules on a closed or liquidated position.");
        }

        if (stopLoss.HasValue && stopLoss.Value <= 0)
        {
            throw new DomainException("StopLoss price must be greater than zero.");
        }

        if (takeProfit.HasValue && takeProfit.Value <= 0)
        {
            throw new DomainException("TakeProfit price must be greater than zero.");
        }

        StopLoss = stopLoss;
        TakeProfit = takeProfit;
    }
}
