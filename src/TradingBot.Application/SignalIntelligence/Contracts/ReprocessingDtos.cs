using System;
using System.Collections.Generic;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public record ReprocessMessageRequestDto(
    string Mode = "REPROCESS_ONLY",
    string TriggeredBy = "Operator (Dashboard)"
);

public record MessageProcessingAttemptDto(
    Guid Id,
    Guid TelegramMessageId,
    int AttemptNumber,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string TriggerType,
    string? TriggeredBy,
    string ProcessingMode,
    string? ParserVersion,
    string? AiModelVersion,
    string Status,
    string? Intent,
    string? Symbol,
    string? Side,
    decimal? EntryPrice,
    decimal? StopLoss,
    string? TakeProfitsJson,
    decimal? Leverage,
    string? SpreadAllowance,
    string? ValidationResult,
    string? RiskResult,
    string? ExecutionResult,
    string? ErrorMessage,
    string? MetadataJson
);

public record ReprocessingResultDto(
    bool Success,
    Guid AttemptId,
    MessageProcessingAttemptDto Attempt,
    MessageProcessingAttemptDto? PreviousAttempt,
    string Message
);

public record ExplicitExecutionRequestDto(
    Guid AttemptId,
    string ConfirmedBy = "Operator (Dashboard)",
    string? ConfirmationNotes = null
);

public record ExplicitExecutionResultDto(
    bool Success,
    Guid AttemptId,
    string Status,
    string? OrderId,
    string Message
);
