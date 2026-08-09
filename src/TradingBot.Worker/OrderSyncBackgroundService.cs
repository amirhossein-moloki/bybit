using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Repositories;
using TradingBot.Application.Interfaces.Streams;

namespace TradingBot.Worker;

public class OrderSyncBackgroundService : BackgroundService
{
    private readonly IOrderStream _orderStream;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderSyncBackgroundService> _logger;
    private readonly TradingBot.Application.Monitoring.IWorkerHealthRegistry _healthRegistry;

    public OrderSyncBackgroundService(
        IOrderStream orderStream,
        IServiceProvider serviceProvider,
        ILogger<OrderSyncBackgroundService> logger,
        TradingBot.Application.Monitoring.IWorkerHealthRegistry healthRegistry)
    {
        _orderStream = orderStream ?? throw new ArgumentNullException(nameof(orderStream));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _healthRegistry.RegisterWorker(nameof(OrderSyncBackgroundService), isCritical: false);
        _logger.LogInformation("OrderSyncBackgroundService: Starting...");

        try
        {
            await _orderStream.SubscribeAsync(stoppingToken);
            _logger.LogInformation("OrderSyncBackgroundService: Subscribed to order stream.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderSyncBackgroundService: Failed to subscribe to order stream.");
        }

        try
        {
            await foreach (var orderUpdate in _orderStream.ReceiveEventsAsync(stoppingToken))
            {
                _healthRegistry.RecordHeartbeat(nameof(OrderSyncBackgroundService), "Running");
                _logger.LogInformation("OrderSyncBackgroundService: Received order update - ClientOrderId: {ClientOrderId}, Status: {Status}, CumExecQty: {CumExecQty}",
                    orderUpdate.ClientOrderId, orderUpdate.Status, orderUpdate.FilledQuantity);

                using var scope = _serviceProvider.CreateScope();
                var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                try
                {
                    await unitOfWork.BeginTransactionAsync(stoppingToken);

                    var order = await orderRepository.GetByClientOrderIdAsync(orderUpdate.ClientOrderId, stoppingToken);
                    if (order != null)
                    {
                        _logger.LogInformation("OrderSyncBackgroundService: Updating database record for order {ClientOrderId} to status {Status}...",
                            order.ClientOrderId, orderUpdate.Status);

                        order.UpdateStatus(orderUpdate.Status);

                        await orderRepository.UpdateAsync(order, stoppingToken);
                        await unitOfWork.SaveChangesAsync(stoppingToken);
                        await unitOfWork.CommitTransactionAsync(stoppingToken);

                        _logger.LogInformation("OrderSyncBackgroundService: Order {ClientOrderId} successfully updated in database.", order.ClientOrderId);
                    }
                    else
                    {
                        _logger.LogWarning("OrderSyncBackgroundService: Order {ClientOrderId} received in stream but not found in database.", orderUpdate.ClientOrderId);
                        await unitOfWork.RollbackTransactionAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OrderSyncBackgroundService: Error processing database update for order {ClientOrderId}.", orderUpdate.ClientOrderId);
                    try
                    {
                        await unitOfWork.RollbackTransactionAsync(stoppingToken);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _healthRegistry.RecordHeartbeat(nameof(OrderSyncBackgroundService), "Stopping");
            _logger.LogInformation("OrderSyncBackgroundService: Cancelled.");
        }
        catch (Exception ex)
        {
            _healthRegistry.RecordHeartbeat(nameof(OrderSyncBackgroundService), "Failed", ex.Message);
            _logger.LogError(ex, "OrderSyncBackgroundService: Exception in order sync receive loop.");
        }

        _healthRegistry.RecordHeartbeat(nameof(OrderSyncBackgroundService), "Stopped");
    }
}
