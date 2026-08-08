using System;
using System.Collections.Generic;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Position
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string? ExchangePositionId { get; private set; }
    public string Symbol { get; private set; }
    public OrderSide Side { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal RemainingQuantity { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? TakeProfit { get; private set; }
    public decimal? Leverage { get; private set; }
    public decimal? Margin { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public decimal UnrealizedPnL { get; private set; }
    public decimal RealizedPnL { get; private set; }
    public decimal Fee { get; private set; }
    public PositionStatus Status { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public ICollection<PositionTarget> Targets { get; private set; } = new List<PositionTarget>();
    public ICollection<PositionEvent> Events { get; private set; } = new List<PositionEvent>();

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

    public Position(
        Guid orderId,
        string symbol,
        OrderSide side,
        decimal entryPrice,
        decimal quantity,
        decimal? stopLoss = null,
        decimal? takeProfit = null,
        string? exchangePositionId = null,
        decimal? leverage = null,
        decimal? margin = null,
        decimal fee = 0m,
        PositionStatus initialStatus = PositionStatus.Open)
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

        if (leverage.HasValue && leverage.Value <= 0)
        {
            throw new DomainException("Leverage must be greater than zero.");
        }

        if (margin.HasValue && margin.Value < 0)
        {
            throw new DomainException("Margin cannot be negative.");
        }

        if (fee < 0)
        {
            throw new DomainException("Fee cannot be negative.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        ExchangePositionId = exchangePositionId;
        Symbol = symbol.Trim().ToUpperInvariant();
        Side = side;
        EntryPrice = entryPrice;
        Quantity = quantity;
        RemainingQuantity = quantity;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        Leverage = leverage;
        Margin = margin;
        CurrentPrice = entryPrice;
        UnrealizedPnL = 0m;
        RealizedPnL = 0m;
        Fee = fee;
        Status = initialStatus;
        OpenedAt = DateTime.UtcNow;
        // Leave UpdatedAt as null initially to match the pattern of Order and support clean nullable concurrency token checks
    }

    public void TransitionTo(PositionStatus newStatus)
    {
        if (Status == newStatus) return;

        bool isValid = Status switch
        {
            PositionStatus.Pending => newStatus == PositionStatus.Open ||
                                      newStatus == PositionStatus.Closed,

            PositionStatus.Open => newStatus == PositionStatus.PartiallyClosed ||
                                   newStatus == PositionStatus.Closed ||
                                   newStatus == PositionStatus.Liquidated,

            PositionStatus.PartiallyClosed => newStatus == PositionStatus.PartiallyClosed ||
                                              newStatus == PositionStatus.Closed ||
                                              newStatus == PositionStatus.Liquidated,

            _ => false
        };

        if (!isValid)
        {
            throw new DomainException($"Invalid transition: Cannot change position status from {Status} to {newStatus}.");
        }

        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;

        if (Status == PositionStatus.Closed || Status == PositionStatus.Liquidated)
        {
            ClosedAt = DateTime.UtcNow;
        }
    }

    public void SetExchangePositionId(string exchangePositionId)
    {
        if (string.IsNullOrWhiteSpace(exchangePositionId))
        {
            throw new DomainException("ExchangePositionId cannot be empty.");
        }
        ExchangePositionId = exchangePositionId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePrice(decimal currentPrice)
    {
        if (Status != PositionStatus.Open && Status != PositionStatus.PartiallyClosed)
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
            UnrealizedPnL = (CurrentPrice - EntryPrice) * RemainingQuantity;
        }
        else
        {
            UnrealizedPnL = (EntryPrice - CurrentPrice) * RemainingQuantity;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void PartialClose(decimal closeQuantity, decimal exitPrice, decimal fee = 0m)
    {
        if (Status != PositionStatus.Open && Status != PositionStatus.PartiallyClosed)
        {
            throw new DomainException($"Invalid transition: Cannot partially close a position in {Status} state.");
        }

        if (closeQuantity <= 0)
        {
            throw new DomainException("Close quantity must be greater than zero.");
        }

        if (closeQuantity > RemainingQuantity)
        {
            throw new DomainException("Cannot close more than the remaining position quantity.");
        }

        if (exitPrice <= 0)
        {
            throw new DomainException("Exit price must be greater than zero.");
        }

        if (fee < 0)
        {
            throw new DomainException("Fee cannot be negative.");
        }

        CurrentPrice = exitPrice;
        RemainingQuantity -= closeQuantity;
        Fee += fee;

        decimal tradePnL;
        if (Side == OrderSide.Buy)
        {
            tradePnL = (exitPrice - EntryPrice) * closeQuantity;
            UnrealizedPnL = (CurrentPrice - EntryPrice) * RemainingQuantity;
        }
        else
        {
            tradePnL = (EntryPrice - exitPrice) * closeQuantity;
            UnrealizedPnL = (EntryPrice - CurrentPrice) * RemainingQuantity;
        }

        RealizedPnL += tradePnL;

        if (RemainingQuantity == 0)
        {
            TransitionTo(PositionStatus.Closed);
        }
        else
        {
            TransitionTo(PositionStatus.PartiallyClosed);
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Close(decimal exitPrice, decimal fee = 0m)
    {
        if (Status != PositionStatus.Open && Status != PositionStatus.PartiallyClosed && Status != PositionStatus.Pending)
        {
            throw new DomainException($"Invalid transition: Cannot close a position that is already in {Status} state.");
        }

        if (exitPrice <= 0)
        {
            throw new DomainException("Exit price must be greater than zero.");
        }

        if (fee < 0)
        {
            throw new DomainException("Fee cannot be negative.");
        }

        CurrentPrice = exitPrice;
        Fee += fee;

        decimal tradePnL;
        if (Side == OrderSide.Buy)
        {
            tradePnL = (exitPrice - EntryPrice) * RemainingQuantity;
        }
        else
        {
            tradePnL = (EntryPrice - exitPrice) * RemainingQuantity;
        }

        RealizedPnL += tradePnL;
        RemainingQuantity = 0m;
        UnrealizedPnL = 0m;

        TransitionTo(PositionStatus.Closed);
    }

    public void Liquidate()
    {
        if (Status != PositionStatus.Open && Status != PositionStatus.PartiallyClosed)
        {
            throw new DomainException($"Invalid transition: Cannot liquidate a position that is in {Status} state.");
        }

        decimal tradePnL;
        if (Side == OrderSide.Buy)
        {
            tradePnL = -EntryPrice * RemainingQuantity; // Total loss of the remaining position
        }
        else
        {
            tradePnL = -EntryPrice * RemainingQuantity; // Total loss of the remaining position
        }

        RealizedPnL += tradePnL;
        RemainingQuantity = 0m;
        UnrealizedPnL = 0m;
        CurrentPrice = 0m;

        TransitionTo(PositionStatus.Liquidated);
    }

    public void UpdateRiskRules(decimal? stopLoss, decimal? takeProfit)
    {
        if (Status != PositionStatus.Open && Status != PositionStatus.PartiallyClosed)
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
        UpdatedAt = DateTime.UtcNow;
    }
}
