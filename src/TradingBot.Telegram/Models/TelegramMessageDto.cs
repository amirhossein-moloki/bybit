using System;

namespace TradingBot.Telegram.Models;

public class TelegramMessageDto
{
    public long ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public int MessageId { get; set; }
    public long SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public bool IsChannel { get; set; }
    public bool IsGroup { get; set; }
    public string RawUpdate { get; set; } = string.Empty;
}
