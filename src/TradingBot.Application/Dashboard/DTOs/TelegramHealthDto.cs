using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TelegramHealthDto(
    string Status,
    DateTime? LastCheck,
    DateTime? LastSuccessfulOperation,
    DateTime? LastFailure
);
