using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class TradeExecutionResult
{
    public bool Success { get; set; }
    public Guid? OrderId { get; set; }
    public string? ExchangeOrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public decimal ExecutedPrice { get; set; }
    public decimal ExecutedQuantity { get; set; }
}
