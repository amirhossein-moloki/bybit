using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class NotificationDeliveryAttempt
{
    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTime AttemptedAt { get; private set; }
    public bool IsSuccess { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    // Required for EF Core
    private NotificationDeliveryAttempt()
    {
        Id = Guid.Empty;
        NotificationId = Guid.Empty;
        AttemptedAt = DateTime.UtcNow;
    }

    public NotificationDeliveryAttempt(
        Guid notificationId,
        int attemptNumber,
        bool isSuccess,
        string? errorCode = null,
        string? errorMessage = null)
    {
        if (notificationId == Guid.Empty)
            throw new DomainException("NotificationId cannot be empty.");
        if (attemptNumber <= 0)
            throw new DomainException("AttemptNumber must be greater than zero.");

        Id = Guid.NewGuid();
        NotificationId = notificationId;
        AttemptNumber = attemptNumber;
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        AttemptedAt = DateTime.UtcNow;
    }
}
