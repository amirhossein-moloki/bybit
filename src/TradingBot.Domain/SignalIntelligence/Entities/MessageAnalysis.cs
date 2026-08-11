using System;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class MessageAnalysis
{
    public Guid Id { get; private set; }
    public Guid TelegramMessageId { get; private set; }
    public MessageType MessageType { get; private set; }
    public decimal Confidence { get; private set; }
    public string ExtractedData { get; private set; }
    public bool AIUsed { get; private set; }
    public DateTime ProcessedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private MessageAnalysis()
    {
        Id = Guid.Empty;
        TelegramMessageId = Guid.Empty;
        MessageType = MessageType.UNKNOWN;
        ExtractedData = string.Empty;
        ProcessedAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    public MessageAnalysis(
        Guid telegramMessageId,
        MessageType messageType,
        decimal confidence,
        string extractedData,
        bool aiUsed,
        DateTime processedAt)
    {
        if (telegramMessageId == Guid.Empty)
        {
            throw new DomainException("TelegramMessageId is required.");
        }

        if (confidence < 0m || confidence > 1m)
        {
            throw new DomainException("Confidence must be between 0 and 1.");
        }

        if (!Enum.IsDefined(typeof(MessageType), messageType))
        {
            throw new DomainException("MessageType is invalid.");
        }

        Id = Guid.NewGuid();
        TelegramMessageId = telegramMessageId;
        MessageType = messageType;
        Confidence = confidence;
        ExtractedData = extractedData ?? "{}";
        AIUsed = aiUsed;
        ProcessedAt = processedAt == default ? DateTime.UtcNow : processedAt;
        CreatedAt = DateTime.UtcNow;
    }
}
