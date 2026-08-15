namespace TradingBot.Telegram.Models;

public class TelegramStatusDto
{
    public bool Connected { get; set; }
    public string Status { get; set; } = "NotConnected";
    public TelegramAccountDto? Account { get; set; }
}
