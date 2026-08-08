using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.ValueObjects;

namespace TradingBot.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public Guid? SignalId { get; private set; }
    public string ClientOrderId { get; private set; }
    public TradingBot.Domain.ValueObjects.Symbol Symbol { get; private set; }
    public OrderSide Side { get; private set; }
    public OrderType Type { get; private set; }
    public Quantity Quantity { get; private set; }
    public Money Price { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? ExchangeOrderId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Required for EF Core
    private Order()
    {
        Id = Guid.Empty;
        ClientOrderId = string.Empty;
        Symbol = null!;
        Quantity = null!;
        Price = null!;
        Status = OrderStatus.Created;
        CreatedAt = DateTime.UtcNow;
    }

    public Order(string clientOrderId, TradingBot.Domain.ValueObjects.Symbol symbol, OrderSide side, OrderType type, Quantity quantity, Money price, Guid? signalId = null)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new DomainException("ClientOrderId cannot be empty.");
        }

        Id = Guid.NewGuid();
        SignalId = signalId;
        ClientOrderId = clientOrderId;
        Symbol = symbol ?? throw new DomainException("Symbol cannot be null.");
        Side = side;
        Type = type;
        Quantity = quantity ?? throw new DomainException("Quantity cannot be null.");
        Price = price ?? throw new DomainException("Price cannot be null.");
        Status = OrderStatus.Created;
        CreatedAt = DateTime.UtcNow;

        if (Type == OrderType.Limit && Price.Amount <= 0)
        {
            throw new DomainException("Price must be greater than zero for Limit orders.");
        }
    }

    public void LinkSignal(Guid signalId)
    {
        if (signalId == Guid.Empty)
        {
            throw new DomainException("SignalId cannot be empty.");
        }
        if (SignalId.HasValue && SignalId.Value != signalId)
        {
            throw new DomainException("Order is already linked to another signal.");
        }
        SignalId = signalId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Submit()
    {
        if (Status != OrderStatus.Created)
        {
            throw new DomainException($"Invalid state transition: Cannot submit order in {Status} status.");
        }

        Status = OrderStatus.Submitted;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Accept(string exchangeOrderId)
    {
        if (Status != OrderStatus.Submitted)
        {
            throw new DomainException($"Invalid state transition: Cannot accept order in {Status} status.");
        }

        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            throw new DomainException("ExchangeOrderId cannot be empty when accepting an order.");
        }

        ExchangeOrderId = exchangeOrderId;
        Status = OrderStatus.Accepted;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFilled()
    {
        if (Status != OrderStatus.Accepted && Status != OrderStatus.PartiallyFilled)
        {
            throw new DomainException($"Invalid state transition: Cannot mark order as filled from {Status} status.");
        }

        Status = OrderStatus.Filled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkPartiallyFilled()
    {
        if (Status != OrderStatus.Accepted && Status != OrderStatus.PartiallyFilled)
        {
            throw new DomainException($"Invalid state transition: Cannot mark order as partially filled from {Status} status.");
        }

        Status = OrderStatus.PartiallyFilled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.Accepted && Status != OrderStatus.PartiallyFilled)
        {
            throw new DomainException($"Invalid state transition: Cannot cancel order in {Status} status.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        if (Status != OrderStatus.Submitted && Status != OrderStatus.Created)
        {
            throw new DomainException($"Invalid state transition: Cannot reject order in {Status} status.");
        }

        Status = OrderStatus.Rejected;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed()
    {
        Status = OrderStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    // Retained for backwards compatibility where direct state assignment may occur in mapping layers,
    // but enforcing state machine rules.
    public void UpdateStatus(OrderStatus newStatus)
    {
        if (Status == newStatus) return;

        switch (newStatus)
        {
            case OrderStatus.Submitted:
                Submit();
                break;
            case OrderStatus.Accepted:
                if (Status == OrderStatus.Created) Submit();
                Accept(ExchangeOrderId ?? "TEMP_EXCHANGE_ID");
                break;
            case OrderStatus.PartiallyFilled:
                if (Status == OrderStatus.Created) Submit();
                if (Status == OrderStatus.Submitted) Accept(ExchangeOrderId ?? "TEMP_EXCHANGE_ID");
                MarkPartiallyFilled();
                break;
            case OrderStatus.Filled:
                if (Status == OrderStatus.Created) Submit();
                if (Status == OrderStatus.Submitted) Accept(ExchangeOrderId ?? "TEMP_EXCHANGE_ID");
                MarkFilled();
                break;
            case OrderStatus.Cancelled:
                if (Status == OrderStatus.Created) Submit();
                if (Status == OrderStatus.Submitted) Accept(ExchangeOrderId ?? "TEMP_EXCHANGE_ID");
                Cancel();
                break;
            case OrderStatus.Rejected:
                Reject("Direct transition to Rejected.");
                break;
            case OrderStatus.Pending:
                Status = OrderStatus.Pending;
                UpdatedAt = DateTime.UtcNow;
                break;
            case OrderStatus.New:
                Status = OrderStatus.New;
                UpdatedAt = DateTime.UtcNow;
                break;
            case OrderStatus.Failed:
                Status = OrderStatus.Failed;
                UpdatedAt = DateTime.UtcNow;
                break;
            default:
                throw new DomainException($"Direct status update to {newStatus} from {Status} is not supported.");
        }
    }
}
