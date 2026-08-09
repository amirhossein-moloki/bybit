namespace TradingBot.Application.Monitoring;

public class NotificationDeliveryResult
{
    public bool Success { get; }
    public bool IsRetryable { get; }
    public string? ErrorCode { get; }
    public string? SafeMessage { get; }

    private NotificationDeliveryResult(bool success, bool isRetryable, string? errorCode, string? safeMessage)
    {
        Success = success;
        IsRetryable = isRetryable;
        ErrorCode = errorCode;
        SafeMessage = safeMessage;
    }

    public static NotificationDeliveryResult AsSuccess() =>
        new(true, false, null, null);

    public static NotificationDeliveryResult AsFailure(bool isRetryable, string? errorCode, string? safeMessage) =>
        new(false, isRetryable, errorCode, safeMessage);
}
