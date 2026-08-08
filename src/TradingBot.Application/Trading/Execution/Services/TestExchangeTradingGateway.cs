using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class TestExchangeTradingGateway : IExchangeTradingGateway
{
    private readonly bool _simulateFailure;
    private readonly string? _forcedErrorCode;
    private readonly string? _forcedErrorMessage;

    public TestExchangeTradingGateway(bool simulateFailure = false, string? forcedErrorCode = null, string? forcedErrorMessage = null)
    {
        _simulateFailure = simulateFailure;
        _forcedErrorCode = forcedErrorCode;
        _forcedErrorMessage = forcedErrorMessage;
    }

    public Task<OrderResult> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<OrderResult>(cancellationToken);
        }

        if (_simulateFailure)
        {
            return Task.FromResult(new OrderResult
            {
                Success = false,
                Status = OrderStatus.Rejected,
                ErrorMessage = _forcedErrorMessage ?? "Simulated exchange failure",
                ErrorCode = _forcedErrorCode ?? "EXCHANGE_ERR_001"
            });
        }

        return Task.FromResult(new OrderResult
        {
            Success = true,
            ExchangeOrderId = $"EX-{Guid.NewGuid():N}",
            Status = OrderStatus.Filled,
            ErrorMessage = string.Empty,
            ErrorCode = null
        });
    }

    public Task<OrderResult> GetOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<OrderResult>(cancellationToken);
        }

        return Task.FromResult(new OrderResult
        {
            Success = true,
            ExchangeOrderId = exchangeOrderId,
            Status = OrderStatus.Filled,
            ErrorMessage = string.Empty,
            ErrorCode = null
        });
    }

    public Task<OrderResult> CancelOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<OrderResult>(cancellationToken);
        }

        return Task.FromResult(new OrderResult
        {
            Success = true,
            ExchangeOrderId = exchangeOrderId,
            Status = OrderStatus.Cancelled,
            ErrorMessage = string.Empty,
            ErrorCode = null
        });
    }
}
