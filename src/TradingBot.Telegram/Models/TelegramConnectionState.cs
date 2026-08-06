namespace TradingBot.Telegram.Models;

public enum TelegramConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Authenticating,
    AuthenticationFailed,
    Error
}
