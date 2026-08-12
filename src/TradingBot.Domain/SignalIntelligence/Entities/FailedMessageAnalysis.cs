using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class FailedMessageAnalysis
{
    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; } // Matches TelegramMessage ID
    public string FailureReason { get; private set; }
    public string Component { get; private set; }
    public int RetryCount { get; private set; }
    public string Status { get; private set; } // Failed, Resolved
    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    // Required for EF Core
    private FailedMessageAnalysis()
    {
        Id = Guid.Empty;
        MessageId = Guid.Empty;
        FailureReason = string.Empty;
        Component = string.Empty;
        Status = "Failed";
        CreatedAt = DateTime.UtcNow;
    }

    public FailedMessageAnalysis(
        Guid messageId,
        string failureReason,
        string component,
        int retryCount,
        string status = "Failed")
    {
        if (messageId == Guid.Empty)
        {
            throw new DomainException("MessageId is required.");
        }

        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new DomainException("FailureReason is required.");
        }

        if (string.IsNullOrWhiteSpace(component))
        {
            throw new DomainException("Component is required.");
        }

        Id = Guid.NewGuid();
        MessageId = messageId;
        FailureReason = failureReason;
        Component = component;
        RetryCount = retryCount;
        Status = status;
        CreatedAt = DateTime.UtcNow;
    }

    public void IncrementRetry()
    {
        RetryCount++;
    }

    public void Resolve()
    {
        Status = "Resolved";
        ResolvedAt = DateTime.UtcNow;
    }
}
