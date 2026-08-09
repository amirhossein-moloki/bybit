namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TelegramStatusDto(
    string TelegramStatus,
    string ConnectionStatus
);
