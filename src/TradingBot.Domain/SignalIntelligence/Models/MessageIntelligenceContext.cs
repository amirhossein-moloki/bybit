using System;
using System.Collections.Generic;

namespace TradingBot.Domain.SignalIntelligence.Models;

public class CurrentMessageContext
{
    public long MessageId { get; set; }
    public long ChannelId { get; set; }
    public long? SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public long? ReplyToMessageId { get; set; }
}

public class ReplyContextInfo
{
    public long ReplyToMessageId { get; set; }
    public string OriginalMessageText { get; set; } = string.Empty;
    public Guid? AssociatedSignalId { get; set; }
    public string? AssociatedSignalStatus { get; set; }
    public string? Symbol { get; set; }
}

public class SignalContextInfo
{
    public Guid Id { get; set; }
    public long MessageId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PositionContextInfo
{
    public Guid PositionId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal CurrentStopLoss { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderContextInfo
{
    public Guid OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ChannelContextInfo
{
    public long ChannelId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ProcessMessages { get; set; }
    public bool ListenForSignals { get; set; }
}

public class MessageIntelligenceContext
{
    public CurrentMessageContext CurrentMessage { get; set; } = new();
    public ReplyContextInfo? ReplyContext { get; set; }
    public List<CurrentMessageContext> ConversationContext { get; set; } = new();
    public List<SignalContextInfo> RelatedSignals { get; set; } = new();
    public List<PositionContextInfo> ActivePositions { get; set; } = new();
    public List<OrderContextInfo> PendingOrders { get; set; } = new();
    public ChannelContextInfo ChannelContext { get; set; } = new();
}
