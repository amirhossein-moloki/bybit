namespace TradingBot.Telegram.Models;

public class MonitoredChannelDto
{
    public string Identifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDynamic { get; set; }
}
