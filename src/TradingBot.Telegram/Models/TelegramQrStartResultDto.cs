namespace TradingBot.Telegram.Models;

public class TelegramQrStartResultDto
{
    public string SessionId { get; set; } = string.Empty;
    public string QrData { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
}
