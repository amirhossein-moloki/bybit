using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record DatabaseHealthDto(
    string Status,
    DateTime? LastCheck,
    long? ResponseTime
);
