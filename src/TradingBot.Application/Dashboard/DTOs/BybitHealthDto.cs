using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record BybitHealthDto(
    BybitServiceStatusDto Rest,
    BybitWebSocketStatusDto WebSocket,
    string AuthenticationStatus
);

public sealed record BybitServiceStatusDto(
    string Status,
    DateTime? LastCheck,
    long? ResponseTime
);

public sealed record BybitWebSocketStatusDto(
    string Status,
    DateTime? ConnectedAt,
    DateTime? LastEventAt,
    DateTime? LastDisconnectAt,
    int? ReconnectCount
);
