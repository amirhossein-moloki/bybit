using System;
using System.Collections.Generic;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Parser.Models;

public class ConversationContext
{
    public long ChannelId { get; set; }
    public List<TelegramMessage> Messages { get; set; } = new();
    public List<SignalContext> ActiveSignals { get; set; } = new();
    public string LastSymbol { get; set; } = string.Empty;
    public Guid LastSignalId { get; set; } = Guid.Empty;
    public string LastAction { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
