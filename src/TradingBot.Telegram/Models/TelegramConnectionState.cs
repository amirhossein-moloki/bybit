namespace TradingBot.Telegram.Models;

public enum TelegramConnectionState
{
    NotConnected,
    Disconnected,
    Connecting,
    Connected,
    Authenticating,
    AuthenticationFailed,
    Listening,
    Reconnecting,
    Error
}
