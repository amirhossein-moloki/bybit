using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class ExecutionResult
{
    public bool Success { get; set; }
    public Guid? OrderId { get; set; }
    public string? ExchangeOrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }

    public static ExecutionResult CreateSuccess(Guid orderId, string exchangeOrderId, string message = "Execution succeeded.")
    {
        return new ExecutionResult
        {
            Success = true,
            OrderId = orderId,
            ExchangeOrderId = exchangeOrderId,
            Status = OrderStatus.Filled, // In early stages, we can assume Filled or Created as completed. Let's use standard states.
            Message = message
        };
    }

    public static ExecutionResult CreateFailure(string message, string? errorCode = null, OrderStatus status = OrderStatus.Rejected)
    {
        return new ExecutionResult
        {
            Success = false,
            Status = status,
            Message = message,
            ErrorCode = errorCode
        };
    }
}
