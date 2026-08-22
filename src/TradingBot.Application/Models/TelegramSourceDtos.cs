using System;
using System.Collections.Generic;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Models;

public sealed record TelegramSourceDto(
    Guid Id,
    long TelegramChatId,
    string Title,
    string? Username,
    string Type,
    bool IsEnabled,
    bool ListenForSignals,
    bool ProcessMessages,
    DateTime? PausedUntil,
    string Status,
    DateTime? LastMessageAt,
    int MessagesToday,
    int SignalsToday,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public sealed record CreateTelegramSourceDto(
    long TelegramChatId,
    string Title,
    string? Username = null,
    string Type = "Channel",
    bool IsEnabled = true,
    bool ListenForSignals = true,
    bool ProcessMessages = true
);

public sealed record UpdateTelegramSourceDto(
    bool? IsEnabled = null,
    bool? ListenForSignals = null,
    bool? ProcessMessages = null,
    int? PauseMinutes = null
);

public sealed record TelegramSourceFilterDto(
    string? Search = null,
    string? Type = null,
    bool? IsEnabled = null,
    bool? ListenForSignals = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record SyncSourcesResultDto(
    int DiscoveredCount,
    int NewCount,
    int UpdatedCount,
    int ErrorCount,
    List<string> DiscoveredTitles
);

public sealed record TestSourceResultDto(
    bool Success,
    bool TelegramConnected,
    bool SourceAccessible,
    bool MessagesReadable,
    bool ListenerConfigured,
    bool SignalProcessingAvailable,
    string Message,
    List<string> Details
);

public sealed record BulkUpdateSourcesDto(
    List<Guid> SourceIds,
    string Action, // "Enable", "Disable", "EnableSignals", "DisableSignals", "Pause"
    int? PauseMinutes = null
);

public sealed record TelegramMessagePreviewDto(
    Guid Id,
    long MessageId,
    long? SenderId,
    string Preview,
    DateTime ReceivedAt,
    bool Processed
);

public sealed record TelegramSignalPreviewDto(
    Guid Id,
    long MessageId,
    string Symbol,
    string Action,
    double Confidence,
    string Status,
    DateTime CreatedAt
);

public sealed record TelegramSourceHealthDto(
    string ConnectionStatus,
    string ListenerState,
    DateTime? LastMessageAt,
    DateTime? LastSignalAt,
    int ProcessingErrors,
    int ReconnectCount
);
