namespace TradingBot.Telegram.Models;

public class TelegramDialogDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsChannel { get; set; }
    public bool IsGroup { get; set; }
    public bool IsMonitored { get; set; }
}
