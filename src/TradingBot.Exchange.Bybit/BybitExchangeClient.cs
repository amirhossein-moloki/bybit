using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Exchange.Bybit;

/// <summary>
/// STAGE 01 PLACEHOLDER ONLY.
/// This implementation represents the project boundary for the Bybit Exchange provider.
/// Real API integration, authentication, and HTTP client logic will be implemented in Stage 02.
/// </summary>
public class BybitExchangeClient : IExchangeClient
{
    private readonly ILogger<BybitExchangeClient> _logger;

    public string ExchangeName => "Bybit";

    public BybitExchangeClient(ILogger<BybitExchangeClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// STAGE 01 PLACEHOLDER: Simulates successful order placement on Bybit with "Filled" status.
    /// No network or API calls are performed at this stage.
    /// </summary>
    public Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Bybit] [STAGE 01 MOCK] PlaceOrderAsync called for {Symbol} {Side} qty {Qty}",
            order.Symbol, order.Side, order.Quantity);

        // Simulate successful order placement with "Filled" status (for Stage 01 testing)
        var updatedOrder = new Order(
            order.ClientOrderId,
            order.Symbol,
            order.Type,
            order.Side,
            order.Price,
            order.Quantity
        );
        updatedOrder.UpdateStatus(OrderStatus.Filled);

        return Task.FromResult(updatedOrder);
    }

    /// <summary>
    /// STAGE 01 PLACEHOLDER: Simulates querying order status on Bybit.
    /// </summary>
    public Task<Order> GetOrderStatusAsync(string clientOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Bybit] [STAGE 01 MOCK] GetOrderStatusAsync called for order {ClientOrderId}", clientOrderId);

        var order = new Order(
            clientOrderId,
            symbol,
            OrderType.Limit,
            SignalType.Buy,
            100,
            1
        );
        order.UpdateStatus(OrderStatus.Filled);

        return Task.FromResult(order);
    }

    /// <summary>
    /// STAGE 01 PLACEHOLDER: Simulates pinging the Bybit endpoint.
    /// </summary>
    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Bybit] [STAGE 01 MOCK] PingAsync called");
        return Task.FromResult(true);
    }
}
