using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Signal
{
    public Guid Id { get; private set; }
    public string Source { get; private set; }
    public string RawMessage { get; private set; }
    public string Symbol { get; private set; }
    public OrderSide Side { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? TakeProfit { get; private set; }
    public int? Leverage { get; private set; }
    public SignalStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public long? TelegramChannelId { get; private set; }
    public long? TelegramMessageId { get; private set; }

    // Validation engine and processing fields
    public string? ValidationStatus { get; private set; }
    public string? ValidationMessage { get; private set; }
    public string? ParserVersion { get; private set; }
    public DateTime? ValidatedAt { get; private set; }

    // Backward compatibility properties
    public SignalType Type { get; private set; }
    public decimal Price { get; private set; }

    // Required for EF Core
    private Signal()
    {
        Id = Guid.Empty;
        Source = string.Empty;
        RawMessage = string.Empty;
        Symbol = string.Empty;
        Side = OrderSide.Buy;
        Type = SignalType.Buy;
        Price = 0m;
        Status = SignalStatus.Received;
        CreatedAt = DateTime.UtcNow;
    }

    // New complete constructor
    public Signal(string source, string rawMessage, string symbol, OrderSide side, decimal entryPrice, decimal quantity, decimal? stopLoss = null, decimal? takeProfit = null, int? leverage = null)
    {
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
            throw new DomainException("StopLoss must be greater than zero.");
        }

        if (takeProfit.HasValue && takeProfit.Value <= 0)
        {
            throw new DomainException("TakeProfit must be greater than zero.");
        }

        if (leverage.HasValue && leverage.Value < 1)
        {
            throw new DomainException("Leverage must be at least 1.");
        }

        Id = Guid.NewGuid();
        Source = source ?? "UNKNOWN";
        RawMessage = rawMessage ?? string.Empty;
        Symbol = symbol.ToUpperInvariant();
        Side = side;
        EntryPrice = entryPrice;
        Price = entryPrice;
        Type = side == OrderSide.Buy ? SignalType.Buy : SignalType.Sell;
        Quantity = quantity;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        Leverage = leverage;
        Status = SignalStatus.Received;
        CreatedAt = DateTime.UtcNow;
    }

    // Constructor for Signal Storage Stage
    public Signal(
        long telegramChannelId,
        long telegramMessageId,
        string rawMessage,
        string symbol,
        OrderSide side,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("Symbol cannot be empty.");
        }

        Id = Guid.NewGuid();
        Source = telegramChannelId.ToString();
        TelegramChannelId = telegramChannelId;
        TelegramMessageId = telegramMessageId;
        RawMessage = rawMessage ?? string.Empty;
        Symbol = symbol.ToUpperInvariant();
        Side = side;
        Type = side == OrderSide.Buy ? SignalType.Buy : SignalType.Sell;
        Status = SignalStatus.Received;
        CreatedAt = createdAt;

        // Use default positive placeholder values for compatibility with domain validation rules
        EntryPrice = 1.0m;
        Price = 1.0m;
        Quantity = 1.0m;
    }

    // Backward compatibility constructor
    public Signal(string symbol, SignalType type, decimal price, decimal quantity)
        : this("LEGACY", $"Legacy signal for {symbol}", symbol, type == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell, price, quantity)
    {
    }

    public void MarkParsing()
    {
        if (Status != SignalStatus.Received)
        {
            throw new DomainException($"Invalid transition: Cannot set status to Parsing when current status is {Status}.");
        }
        Status = SignalStatus.Parsing;
    }

    public void MarkParsed()
    {
        if (Status != SignalStatus.Parsing && Status != SignalStatus.Received)
        {
            throw new DomainException($"Invalid transition: Cannot set status to Parsed when current status is {Status}.");
        }
        Status = SignalStatus.Parsed;
    }

    public void MarkValidated()
    {
        if (Status != SignalStatus.Parsed && Status != SignalStatus.Parsing && Status != SignalStatus.Received)
        {
            throw new DomainException($"Invalid transition: Cannot set status to Validated when current status is {Status}.");
        }
        Status = SignalStatus.Validated;
    }

    public void MarkReadyForRiskEngine()
    {
        if (Status != SignalStatus.Validated && Status != SignalStatus.Parsed && Status != SignalStatus.Parsing && Status != SignalStatus.Received)
        {
            throw new DomainException($"Invalid transition: Cannot set status to ReadyForRiskEngine when current status is {Status}.");
        }
        Status = SignalStatus.ReadyForRiskEngine;
    }

    public void MarkRejected()
    {
        if (Status == SignalStatus.Executed)
        {
            throw new DomainException("Invalid transition: Cannot reject an already executed signal.");
        }
        Status = SignalStatus.Rejected;
    }

    public void MarkExecuted()
    {
        if (Status != SignalStatus.ReadyForRiskEngine && Status != SignalStatus.Validated && Status != SignalStatus.Parsed && Status != SignalStatus.Received)
        {
            throw new DomainException($"Invalid transition: Cannot execute signal in {Status} status.");
        }
        Status = SignalStatus.Executed;
    }

    public void SetValidationResult(string status, string? message, string parserVersion)
    {
        ValidationStatus = status;
        ValidationMessage = message;
        ParserVersion = parserVersion;
        ValidatedAt = DateTime.UtcNow;
    }

    // Setters to allow updating properties during/after parsing
    public void UpdateParsedDetails(string symbol, OrderSide side, decimal entryPrice, decimal? stopLoss, decimal? takeProfit, int? leverage)
    {
        Symbol = symbol?.ToUpperInvariant() ?? Symbol;
        Side = side;
        Type = side == OrderSide.Buy ? SignalType.Buy : SignalType.Sell;
        EntryPrice = entryPrice;
        Price = entryPrice;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        Leverage = leverage;
    }
}
