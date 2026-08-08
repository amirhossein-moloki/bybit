using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Trade
{
    public Guid Id { get; private set; }

    // Existing fields for individual fills
    public string TradeId { get; private set; }
    public string OrderId { get; private set; }
    public string Symbol { get; private set; }
    public SignalType Side { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Fee { get; private set; }
    public string FeeAsset { get; private set; }
    public DateTime ExecutedAt { get; private set; }

    // New fields for Phase 2: realized execution/closure history
    public Guid? PositionId { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal? ExitPrice { get; private set; }
    public decimal? ProfitLoss { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    // Advanced Trade Result fields
    public decimal? FundingFee { get; private set; }
    public decimal? NetPnL { get; private set; }
    public CloseReason? CloseReason { get; private set; }
    public DateTime? OpenedAt { get; private set; }

    public decimal? GrossPnL => ProfitLoss;
    public decimal? TradingFee => Fee;

    // Required for EF Core
    private Trade()
    {
        Id = Guid.Empty;
        TradeId = string.Empty;
        OrderId = string.Empty;
        Symbol = string.Empty;
        Side = SignalType.Buy;
        FeeAsset = string.Empty;
        ExecutedAt = DateTime.UtcNow;
    }

    // Existing constructor (retained for backward compatibility)
    public Trade(string tradeId, string orderId, string symbol, SignalType side, decimal price, decimal quantity, decimal fee, string feeAsset)
    {
        if (string.IsNullOrWhiteSpace(tradeId))
        {
            throw new DomainException("TradeId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new DomainException("OrderId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("Symbol cannot be empty.");
        }

        if (price <= 0)
        {
            throw new DomainException("Price must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        TradeId = tradeId;
        OrderId = orderId;
        Symbol = symbol.ToUpperInvariant();
        Side = side;
        Price = price;
        Quantity = quantity;
        Fee = fee;
        FeeAsset = feeAsset;
        ExecutedAt = DateTime.UtcNow;

        // Map to Phase 2 equivalent properties
        EntryPrice = price;
        ExitPrice = null;
        ProfitLoss = null;
        ClosedAt = null;
        PositionId = null;
    }

    // New constructor representing completed realized position closure/execution history (backward compatible)
    public Trade(Guid positionId, decimal entryPrice, decimal exitPrice, decimal quantity, decimal profitLoss, decimal fee, DateTime closedAt)
    {
        if (positionId == Guid.Empty)
        {
            throw new DomainException("PositionId cannot be empty.");
        }

        if (entryPrice <= 0)
        {
            throw new DomainException("EntryPrice must be greater than zero.");
        }

        if (exitPrice <= 0)
        {
            throw new DomainException("ExitPrice must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        PositionId = positionId;
        EntryPrice = entryPrice;
        ExitPrice = exitPrice;
        Quantity = quantity;
        ProfitLoss = profitLoss;
        Fee = fee;
        ClosedAt = closedAt;

        // Map to backward-compatible fields
        TradeId = "COMPLETED-" + Id.ToString("N")[..8].ToUpperInvariant();
        OrderId = string.Empty;
        Symbol = string.Empty;
        Side = SignalType.Buy;
        Price = exitPrice;
        FeeAsset = "USDT";
        ExecutedAt = closedAt;
    }

    // Advanced constructor representing completed realized trade results with fees and metrics
    public Trade(
        Guid positionId,
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity,
        decimal grossPnL,
        decimal tradingFee,
        decimal fundingFee,
        decimal netPnL,
        CloseReason closeReason,
        DateTime openedAt,
        DateTime closedAt)
    {
        if (positionId == Guid.Empty)
        {
            throw new DomainException("PositionId cannot be empty.");
        }

        if (entryPrice <= 0)
        {
            throw new DomainException("EntryPrice must be greater than zero.");
        }

        if (exitPrice <= 0)
        {
            throw new DomainException("ExitPrice must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        PositionId = positionId;
        EntryPrice = entryPrice;
        ExitPrice = exitPrice;
        Quantity = quantity;
        ProfitLoss = grossPnL;
        Fee = tradingFee;
        FundingFee = fundingFee;
        NetPnL = netPnL;
        CloseReason = closeReason;
        OpenedAt = openedAt;
        ClosedAt = closedAt;

        // Map to backward-compatible fields
        TradeId = "COMPLETED-" + Id.ToString("N")[..8].ToUpperInvariant();
        OrderId = string.Empty;
        Symbol = string.Empty;
        Side = SignalType.Buy;
        Price = exitPrice;
        FeeAsset = "USDT";
        ExecutedAt = closedAt;
    }
}
