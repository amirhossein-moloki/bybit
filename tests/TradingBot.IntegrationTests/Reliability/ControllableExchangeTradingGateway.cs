using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;

namespace TradingBot.IntegrationTests.Reliability;

public class ControllableExchangeTradingGateway : IExchangeTradingGateway
{
    private readonly FailureSimulator _simulator;
    private readonly ConcurrentDictionary<string, OrderResult> _exchangeOrders = new();
    private readonly ConcurrentDictionary<string, OrderResult> _clientOrderIdsToOrders = new();

    public ControllableExchangeTradingGateway(FailureSimulator simulator)
    {
        _simulator = simulator;
    }

    public void SeedExchangeOrder(string clientOrderId, OrderResult result)
    {
        _clientOrderIdsToOrders[clientOrderId] = result;
        if (!string.IsNullOrEmpty(result.ExchangeOrderId))
        {
            _exchangeOrders[result.ExchangeOrderId] = result;
        }
    }

    public void ClearExchangeOrders()
    {
        _exchangeOrders.Clear();
        _clientOrderIdsToOrders.Clear();
    }

    public async Task<OrderResult> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        var key = "Bybit_REST";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            if (failureType == FailureType.UnknownState)
            {
                // UnknownState: order succeeds on exchange but throws an exception to caller
                var successfulOrder = new OrderResult
                {
                    Success = true,
                    ExchangeOrderId = "EX-UNINTENDED-SUCCESS-" + Guid.NewGuid().ToString().Substring(0, 8),
                    Status = OrderStatus.New,
                    ExecutedPrice = request.Price,
                    ExecutedQuantity = request.Quantity
                };

                if (!string.IsNullOrEmpty(request.ClientOrderId))
                {
                    _clientOrderIdsToOrders[request.ClientOrderId] = successfulOrder;
                }
                _exchangeOrders[successfulOrder.ExchangeOrderId!] = successfulOrder;

                _simulator.HandleFailureType(FailureType.Timeout, "CreateOrder");
            }

            _simulator.HandleFailureType(failureType, "CreateOrder");
        }

        // Standard flow
        var exchangeOrderId = "EX-" + Guid.NewGuid().ToString().Substring(0, 8);
        var orderResult = new OrderResult
        {
            Success = true,
            ExchangeOrderId = exchangeOrderId,
            Status = OrderStatus.New,
            ExecutedPrice = request.Price,
            ExecutedQuantity = request.Quantity
        };

        if (!string.IsNullOrEmpty(request.ClientOrderId))
        {
            _clientOrderIdsToOrders[request.ClientOrderId] = orderResult;
        }
        _exchangeOrders[exchangeOrderId] = orderResult;

        return await Task.FromResult(orderResult);
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        var key = "Bybit_REST";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            _simulator.HandleFailureType(failureType, "GetOrder");
        }

        // Support lookup by clientOrderId or exchangeOrderId
        if (_clientOrderIdsToOrders.TryGetValue(exchangeOrderId, out var resultByClient))
        {
            return await Task.FromResult(resultByClient);
        }

        if (_exchangeOrders.TryGetValue(exchangeOrderId, out var resultByEx))
        {
            return await Task.FromResult(resultByEx);
        }

        return await Task.FromResult(new OrderResult
        {
            Success = false,
            ErrorCode = "ORDER_NOT_FOUND",
            ErrorMessage = "Order not found on exchange."
        });
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        var key = "Bybit_REST";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            _simulator.HandleFailureType(failureType, "CancelOrder");
        }

        if (_exchangeOrders.TryGetValue(exchangeOrderId, out var order))
        {
            order.Status = OrderStatus.Cancelled;
            return await Task.FromResult(new OrderResult { Success = true, Status = OrderStatus.Cancelled });
        }

        return await Task.FromResult(new OrderResult { Success = false, ErrorMessage = "Order not found" });
    }

    public async Task<OrderResult> SetTradingStopAsync(
        string symbol,
        OrderSide side,
        decimal? stopLoss,
        decimal? takeProfit,
        CancellationToken cancellationToken = default)
    {
        var key = "Bybit_REST";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            _simulator.HandleFailureType(failureType, "SetTradingStop");
        }

        return await Task.FromResult(new OrderResult
        {
            Success = true,
            Status = OrderStatus.Filled
        });
    }
}
