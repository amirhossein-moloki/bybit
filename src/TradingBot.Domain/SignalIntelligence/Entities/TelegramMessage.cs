using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class TelegramMessage
{
    public Guid Id { get; private set; }
    public long ChannelId { get; private set; }
    public long MessageId { get; private set; }
    public long? SenderId { get; private set; }
    public string Content { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public bool Processed { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private TelegramMessage()
    {
        Id = Guid.Empty;
        Content = string.Empty;
        ReceivedAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    public TelegramMessage(
        long channelId,
        long messageId,
        long? senderId,
        string content,
        DateTime receivedAt)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException("Content cannot be empty.");
        }

        if (channelId == 0)
        {
            throw new DomainException("ChannelId is required.");
        }

        if (messageId <= 0)
        {
            throw new DomainException("MessageId is required.");
        }

        Id = Guid.NewGuid();
        ChannelId = channelId;
        MessageId = messageId;
        SenderId = senderId;
        Content = content;
        ReceivedAt = receivedAt == default ? DateTime.UtcNow : receivedAt;
        Processed = false;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkProcessed()
    {
        Processed = true;
    }
}
