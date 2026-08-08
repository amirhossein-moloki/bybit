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
}
