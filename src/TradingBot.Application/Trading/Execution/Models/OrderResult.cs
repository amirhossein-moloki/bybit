using TradingBot.Domain.Enums;
using TradingBot.Application.Trading.Execution.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class OrderResult
{
    public bool Success { get; set; }
    public string? ExchangeOrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public ExchangeErrorType? ErrorType { get; set; }

    // Extended Phase 06 Stage 04 properties
    public decimal ExecutedQuantity { get; set; }
    public decimal ExecutedPrice { get; set; }
    public decimal RemainingQuantity { get; set; }
}
