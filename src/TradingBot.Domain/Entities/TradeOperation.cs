using System;

namespace TradingBot.Domain.Entities;

public class TradeOperation
{
    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string OperationType { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? ExternalId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureCode { get; private set; }

    // Required for EF Core
    private TradeOperation()
    {
    }

    public TradeOperation(Guid id, string idempotencyKey, string operationType, string correlationId, string status)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("IdempotencyKey cannot be empty.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(operationType))
            throw new ArgumentException("OperationType cannot be empty.", nameof(operationType));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status cannot be empty.", nameof(status));

        Id = id;
        IdempotencyKey = idempotencyKey;
        OperationType = operationType;
        CorrelationId = correlationId;
        Status = status;
        CreatedAt = DateTime.UtcNow;
    }

    public TradeOperation(string idempotencyKey, string operationType, string correlationId, string status)
        : this(Guid.NewGuid(), idempotencyKey, operationType, correlationId, status)
    {
    }

    public void UpdateStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status cannot be empty.", nameof(status));

        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompleted(string? externalId = null)
    {
        Status = "Completed";
        ExternalId = externalId ?? ExternalId;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string? failureCode)
    {
        Status = "Failed";
        FailureCode = failureCode;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetExternalId(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("ExternalId cannot be empty.", nameof(externalId));

        ExternalId = externalId;
        UpdatedAt = DateTime.UtcNow;
    }
}
