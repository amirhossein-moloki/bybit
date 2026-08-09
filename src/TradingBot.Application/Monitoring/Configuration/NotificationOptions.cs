using System;
using System.Collections.Generic;

namespace TradingBot.Application.Monitoring.Configuration;

public class NotificationOptions
{
    public bool Enabled { get; set; } = true;
    public TelegramNotificationSettings Telegram { get; set; } = new();
    public NotificationEvents Events { get; set; } = new();

    public void Validate()
    {
        if (!Enabled) return;

        if (Telegram.Enabled)
        {
            if (string.IsNullOrWhiteSpace(Telegram.ChatId))
                throw new ArgumentException("Telegram ChatId cannot be empty when Telegram notifications are enabled.");
            if (Telegram.RetryCount < 0)
                throw new ArgumentException("Notification retry count cannot be negative.");
            if (Telegram.InitialRetryDelaySeconds <= 0)
                throw new ArgumentException("Initial retry delay must be positive.");
            if (Telegram.MaxRetryDelaySeconds <= 0)
                throw new ArgumentException("Maximum retry delay must be positive.");
        }
    }
}

public class TelegramNotificationSettings
{
    public bool Enabled { get; set; } = true;
    public string ChatId { get; set; } = "default-chat-id";
    public int RetryCount { get; set; } = 3;
    public int InitialRetryDelaySeconds { get; set; } = 2;
    public int MaxRetryDelaySeconds { get; set; } = 60;
}

public class NotificationEvents
{
    public bool ApplicationStarted { get; set; } = true;
    public bool ApplicationStopped { get; set; } = true;
    public bool BybitDisconnected { get; set; } = true;
    public bool BybitConnectionRestored { get; set; } = true;
    public bool OrderFilled { get; set; } = true;
    public bool OrderRejected { get; set; } = true;
    public bool PositionOpened { get; set; } = true;
    public bool PositionClosed { get; set; } = true;
    public bool ApplicationError { get; set; } = true;
    public bool CriticalError { get; set; } = true;
    public bool WorkerFailed { get; set; } = true;

    public bool IsEnabled(string eventType)
    {
        return eventType switch
        {
            "ApplicationStarted" => ApplicationStarted,
            "ApplicationStopped" => ApplicationStopped,
            "BybitDisconnected" => BybitDisconnected,
            "BybitConnectionRestored" => BybitConnectionRestored,
            "OrderFilled" => OrderFilled,
            "OrderRejected" => OrderRejected,
            "PositionOpened" => PositionOpened,
            "PositionClosed" => PositionClosed,
            "ApplicationError" => ApplicationError,
            "CriticalError" => CriticalError,
            "WorkerFailed" => WorkerFailed,
            _ => false
        };
    }
}
