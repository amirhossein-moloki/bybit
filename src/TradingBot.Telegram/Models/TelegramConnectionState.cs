namespace TradingBot.Telegram.Models;

public enum TelegramConnectionState
{
    NotConnected,
    Disconnected,
    Connecting,
    Connected,
    Authenticating,
    AuthenticationFailed,
    RequiresAuthentication,
    Listening,
    Reconnecting,
    Error
}
