using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using AppExcept = TradingBot.Application.Exceptions.ApplicationException;

namespace TradingBot.Application.Services;

public class SignalProcessor : ISignalProcessor
{
    private readonly ISignalRepository _signalRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IExchangeClient _exchangeClient;
    private readonly ILogger<SignalProcessor> _logger;

    public SignalProcessor(
        ISignalRepository signalRepository,
        IOrderRepository orderRepository,
        IExchangeClient exchangeClient,
        ILogger<SignalProcessor> logger)
    {
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _exchangeClient = exchangeClient ?? throw new ArgumentNullException(nameof(exchangeClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing signal for symbol {Symbol}, action {Type}, price {Price}, qty {Qty}",
            signal.Symbol, signal.Type, signal.Price, signal.Quantity);

        try
        {
            // 1. Save Signal
            await _signalRepository.SaveAsync(signal, cancellationToken);

            // 2. Create Order
            var clientOrderId = $"BOT-{Guid.NewGuid():N}";
            var order = new Order(
                clientOrderId,
                signal.Symbol,
                OrderType.Limit,
                signal.Type,
                signal.Price,
                signal.Quantity
            );

            await _orderRepository.SaveAsync(order, cancellationToken);

            // 3. Dispatch to Exchange
            _logger.LogInformation("Placing order on exchange {ExchangeName} for client order id {ClientOrderId}",
                _exchangeClient.ExchangeName, clientOrderId);

            var placedOrder = await _exchangeClient.PlaceOrderAsync(order, cancellationToken);

            // 4. Update status and save
            order.UpdateStatus(placedOrder.Status);
            await _orderRepository.SaveAsync(order, cancellationToken);

            _logger.LogInformation("Successfully processed signal, order status is now {Status}", order.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process signal for symbol {Symbol}", signal.Symbol);
            throw new AppExcept($"Failed to process signal: {ex.Message}", ex);
        }
    }
}
