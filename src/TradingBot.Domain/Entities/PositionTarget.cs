using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class PositionTarget
{
    public Guid Id { get; private set; }
    public Guid PositionId { get; private set; }
    public int TargetNumber { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Percentage { get; private set; }
    public string Status { get; private set; }
    public DateTime? ExecutedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private PositionTarget()
    {
        Id = Guid.Empty;
        PositionId = Guid.Empty;
        Status = "Pending";
        CreatedAt = DateTime.UtcNow;
    }

    public PositionTarget(Guid positionId, int targetNumber, decimal price, decimal quantity, decimal percentage, string status = "Pending")
    {
        if (targetNumber <= 0)
        {
            throw new DomainException("TargetNumber must be greater than zero.");
        }

        if (price <= 0)
        {
            throw new DomainException("Price must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        if (percentage <= 0 || percentage > 100)
        {
            throw new DomainException("Percentage must be between 0 and 100.");
        }

        Id = Guid.Empty; // Let EF Core generate the Guid automatically on Add to prevent tracking conflicts
        PositionId = positionId;
        TargetNumber = targetNumber;
        Price = price;
        Quantity = quantity;
        Percentage = percentage;
        Status = status;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetPositionId(Guid positionId)
    {
        if (positionId == Guid.Empty)
        {
            throw new DomainException("PositionId cannot be empty.");
        }
        PositionId = positionId;
    }

    public void MarkExecuted()
    {
        Status = "Executed";
        ExecutedAt = DateTime.UtcNow;
    }
}
