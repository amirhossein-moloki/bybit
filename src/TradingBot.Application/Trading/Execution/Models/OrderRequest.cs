using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class OrderRequest
{
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public Guid? SignalId { get; set; }
    public Guid? RiskEvaluationId { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public bool ReduceOnly { get; set; }
}
