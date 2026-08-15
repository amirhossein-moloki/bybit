namespace TradingBot.Telegram.Models;

public class TelegramQrStatusDto
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = "WaitingForScan";
    public string? QrData { get; set; }
    public string? ExpiresAt { get; set; }
    public TelegramAccountDto? Account { get; set; }
    public string? Error { get; set; }
}
