using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class MessageProcessingAttempt
{
    public Guid Id { get; private set; }
    public Guid TelegramMessageId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string TriggerType { get; private set; } = "ManualReprocess";
    public string? TriggeredBy { get; private set; }
    public string ProcessingMode { get; private set; } = "REPROCESS_ONLY";
    public string? ParserVersion { get; private set; }
    public string? AiModelVersion { get; private set; }
    public string Status { get; private set; } = "InProgress";
    public string? Intent { get; private set; }
    public string? Symbol { get; private set; }
    public string? Side { get; private set; }
    public decimal? EntryPrice { get; private set; }
    public decimal? StopLoss { get; private set; }
    public string? TakeProfitsJson { get; private set; }
    public decimal? Leverage { get; private set; }
    public string? SpreadAllowance { get; private set; }
    public string? ValidationResult { get; private set; }
    public string? RiskResult { get; private set; }
    public string? ExecutionResult { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Private constructor required for EF Core
    private MessageProcessingAttempt()
    {
        Id = Guid.Empty;
        TelegramMessageId = Guid.Empty;
        AttemptNumber = 1;
        StartedAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    public MessageProcessingAttempt(
        Guid telegramMessageId,
        int attemptNumber,
        string triggerType,
        string? triggeredBy = null,
        string processingMode = "REPROCESS_ONLY",
        string? parserVersion = "v1.0",
        string? aiModelVersion = null)
    {
        if (telegramMessageId == Guid.Empty)
        {
            throw new DomainException("TelegramMessageId is required.");
        }

        if (attemptNumber <= 0)
        {
            throw new DomainException("AttemptNumber must be greater than zero.");
        }

        Id = Guid.NewGuid();
        TelegramMessageId = telegramMessageId;
        AttemptNumber = attemptNumber;
        StartedAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
        TriggerType = string.IsNullOrWhiteSpace(triggerType) ? "ManualReprocess" : triggerType;
        TriggeredBy = triggeredBy ?? "Operator";
        ProcessingMode = string.IsNullOrWhiteSpace(processingMode) ? "REPROCESS_ONLY" : processingMode;
        ParserVersion = parserVersion ?? "v1.0";
        AiModelVersion = aiModelVersion;
        Status = "InProgress";
        ExecutionResult = "NoOrder";
    }

    public void Complete(
        string intent,
        string? symbol,
        string? side,
        decimal? entryPrice,
        decimal? stopLoss,
        string? takeProfitsJson,
        decimal? leverage,
        string? spreadAllowance,
        string validationResult,
        string riskResult,
        string executionResult = "NoOrder",
        string? metadataJson = null)
    {
        Intent = intent;
        Symbol = symbol;
        Side = side;
        EntryPrice = entryPrice;
        StopLoss = stopLoss;
        TakeProfitsJson = takeProfitsJson;
        Leverage = leverage;
        SpreadAllowance = spreadAllowance;
        ValidationResult = validationResult;
        RiskResult = riskResult;
        ExecutionResult = executionResult;
        MetadataJson = metadataJson;
        Status = validationResult == "Expired" || riskResult == "Expired" ? "Expired" : "Completed";
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = "Failed";
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }

    public void UpdateExecutionResult(string executionResult, string? metadataJson = null)
    {
        ExecutionResult = executionResult;
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            MetadataJson = metadataJson;
        }
    }
}
