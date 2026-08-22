using System.Collections.Generic;

namespace TradingBot.Telegram.Configuration;

public class TelegramOptions
{
    public string ApiId { get; set; } = string.Empty;
    public string ApiHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string SessionPath { get; set; } = "/app/data/telegram/session";
    public string ProxyUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<string> Channels { get; set; } = new();
}
