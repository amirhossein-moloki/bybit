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

    // Extended Lifecycle Tracking Fields
    public TelegramMessageIntent Intent { get; private set; }
    public string? Action { get; private set; }
    public string? TargetSymbol { get; private set; }
    public decimal ConfidenceScore => Confidence;
    public string ProcessingStatus { get; private set; } = "Completed";
    public string ExtractedMetadata { get; private set; } = "{}";

    // Required for EF Core
    private MessageAnalysis()
    {
        Id = Guid.Empty;
        TelegramMessageId = Guid.Empty;
        MessageType = MessageType.UNKNOWN;
        ExtractedData = string.Empty;
        ProcessedAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
        Intent = TelegramMessageIntent.Unknown;
        ProcessingStatus = "Completed";
        ExtractedMetadata = "{}";
    }

    public MessageAnalysis(
        Guid telegramMessageId,
        MessageType messageType,
        decimal confidence,
        string extractedData,
        bool aiUsed,
        DateTime processedAt)
        : this(
              telegramMessageId,
              messageType,
              confidence,
              extractedData,
              aiUsed,
              processedAt,
              MapMessageTypeToIntent(messageType),
              null,
              null,
              "Completed",
              extractedData)
    {
    }

    public MessageAnalysis(
        Guid telegramMessageId,
        MessageType messageType,
        decimal confidence,
        string extractedData,
        bool aiUsed,
        DateTime processedAt,
        TelegramMessageIntent intent,
        string? action = null,
        string? targetSymbol = null,
        string processingStatus = "Completed",
        string? extractedMetadata = null)
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

        Intent = intent;
        Action = action;
        TargetSymbol = targetSymbol;
        ProcessingStatus = string.IsNullOrWhiteSpace(processingStatus) ? "Completed" : processingStatus;
        ExtractedMetadata = extractedMetadata ?? extractedData ?? "{}";
    }

    public void UpdateProcessingStatus(string status, string? metadata = null)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProcessingStatus = status;
        }

        if (metadata != null)
        {
            ExtractedMetadata = metadata;
        }
    }

    public void SetExecutionDetails(TelegramMessageIntent intent, string? action, string? targetSymbol, string processingStatus, string? metadata = null)
    {
        Intent = intent;
        Action = action;
        if (!string.IsNullOrWhiteSpace(targetSymbol))
        {
            TargetSymbol = targetSymbol;
        }
        ProcessingStatus = processingStatus;
        if (metadata != null)
        {
            ExtractedMetadata = metadata;
        }
    }

    public static TelegramMessageIntent MapMessageTypeToIntent(MessageType messageType) => messageType switch
    {
        MessageType.SIGNAL => TelegramMessageIntent.EntrySignal,
        MessageType.TRADE_UPDATE => TelegramMessageIntent.PositionManagement,
        MessageType.CANCEL_COMMAND => TelegramMessageIntent.ExitSignal,
        MessageType.ANALYSIS => TelegramMessageIntent.MarketAnalysis,
        MessageType.STATUS_UPDATE => TelegramMessageIntent.Informational,
        MessageType.GENERAL_MESSAGE => TelegramMessageIntent.Informational,
        _ => TelegramMessageIntent.Unknown
    };
}
