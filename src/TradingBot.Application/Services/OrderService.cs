using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;

namespace TradingBot.Application.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IExchangeClient _exchangeClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IExchangeClient exchangeClient,
        IUnitOfWork unitOfWork,
        ILogger<OrderService> _logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _exchangeClient = exchangeClient ?? throw new ArgumentNullException(nameof(exchangeClient));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        this._logger = _logger ?? throw new ArgumentNullException(nameof(_logger));
    }

    public async Task<Order> CreateOrderAsync(
        string symbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating order for symbol {Symbol}, side {Side}, type {Type}, quantity {Quantity}, price {Price}",
            symbol, side, type, quantity, price);

        Order order;
        bool placementFailed = false;
        Exception? placementEx = null;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var clientOrderId = $"BOT-{Guid.NewGuid():N}";
            order = new Order(
                clientOrderId,
                new TradingBot.Domain.ValueObjects.Symbol(symbol),
                side,
                type,
                new Quantity(quantity),
                new Money(price)
            );

            // 1. Save Pending Order
            await _orderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 2. Submit order (transition to Submitted)
            order.Submit();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            try
            {
                // 3. Send to Exchange
                var placedOrder = await _exchangeClient.PlaceOrderAsync(order, cancellationToken);

                // 4. Update status with exchange response (transition to Accepted)
                order.Accept(placedOrder.ExchangeOrderId ?? "TEMP_EXCHANGE_ID");
                await _orderRepository.UpdateAsync(order, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                placementFailed = true;
                placementEx = ex;
                _logger.LogError(ex, "Exchange order placement failed for client order id {ClientOrderId}. Rejecting order.", order.ClientOrderId);
                order.Reject(ex.Message);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // Commit transaction to keep state consistent
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute create order transaction. Rolling back.");
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        if (placementFailed && placementEx != null)
        {
            throw placementEx;
        }

        return order;
    }

    public async Task<Order> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling order with ID {OrderId}", orderId);

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException($"Order with ID {orderId} not found.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            order.Cancel();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order with ID {OrderId}. Rolling back.", orderId);
            try
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }
            catch
            {
                // Ignore
            }
            throw;
        }
    }

    public async Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.GetByIdAsync(orderId, cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(cancellationToken);
    }
}
