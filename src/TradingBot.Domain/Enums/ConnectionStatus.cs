namespace TradingBot.Domain.Enums;

public enum ConnectionStatus
{
    Connected,
    Disconnected,
    Connecting,
    Reconnecting,
    Failed,
    Unknown
}
