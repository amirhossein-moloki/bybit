namespace TradingBot.Application.Dashboard.DTOs;

public sealed record ExchangeStatusDto(
    string ExchangeStatus,
    string ConnectionStatus
);
