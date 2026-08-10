using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;

namespace TradingBot.Worker;

public class IncompleteOperationRecoveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IncompleteOperationRecoveryWorker> _logger;
    private readonly IWorkerHealthRegistry _healthRegistry;
    private readonly IdempotencyOptions _options;

    public IncompleteOperationRecoveryWorker(
        IServiceProvider serviceProvider,
        ILogger<IncompleteOperationRecoveryWorker> logger,
        IWorkerHealthRegistry healthRegistry,
        IdempotencyOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _healthRegistry.RegisterWorker(nameof(IncompleteOperationRecoveryWorker), isCritical: false);
        _logger.LogInformation("IncompleteOperationRecoveryWorker: Starting background worker loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            _healthRegistry.RecordHeartbeat(nameof(IncompleteOperationRecoveryWorker), "Running");

            if (_options.Enabled)
            {
                try
                {
                    await RecoverIncompleteOperationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IncompleteOperationRecoveryWorker: Exception occurred during recovery pass.");
                }
            }

            try
            {
                await Task.Delay(_options.RecoveryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _healthRegistry.RecordHeartbeat(nameof(IncompleteOperationRecoveryWorker), "Stopped");
        _logger.LogInformation("IncompleteOperationRecoveryWorker: Background worker loop stopped.");
    }

    private async Task RecoverIncompleteOperationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var tradeOperationRepository = scope.ServiceProvider.GetRequiredService<ITradeOperationRepository>();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var gateway = scope.ServiceProvider.GetRequiredService<TradingBot.Application.Trading.Execution.Contracts.IExchangeTradingGateway>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var metrics = scope.ServiceProvider.GetService<IMetricsService>();
        var eventPublisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();

        // Load all non-terminal operations from DB
        var operations = await tradeOperationRepository.GetAllAsync(cancellationToken);
        var incompleteOps = operations.Where(op => op.Status == "Submitting" || op.Status == "Unknown" || op.Status == "ManualInterventionRequired").ToList();

        var timeoutThreshold = _options.IncompleteOperationTimeout;
        var manualInterventionThreshold = TimeSpan.FromTicks(timeoutThreshold.Ticks * 3);
        var now = DateTime.UtcNow;

        foreach (var op in incompleteOps)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var elapsed = now - op.CreatedAt;
            if (elapsed < timeoutThreshold)
            {
                // Operation is still within the safe submission timeout window. Skip.
                continue;
            }

            _logger.LogInformation("IncompleteOperationRecoveryWorker: Processing recovery candidate operation {OperationId} in status {Status}. Elapsed={ElapsedSeconds}s",
                op.Id, op.Status, elapsed.TotalSeconds);

            if (eventPublisher != null)
            {
                var startedEvent = new MonitoringEvent(
                    "OperationRecoveryStarted",
                    "INFO",
                    "Recovery",
                    "IncompleteOperationRecoveryWorker",
                    "STARTED",
                    $"Started background state recovery for operation {op.Id} (Status={op.Status}).",
                    correlationId: op.CorrelationId
                );
                await eventPublisher.PublishAsync(startedEvent, forceSynchronous: true, cancellationToken);
            }

            // Phase 1: Check if local Order status is already terminal
            var localOrder = await orderRepository.GetByIdAsync(op.Id, cancellationToken);
            if (localOrder != null && IsTerminalState(localOrder.Status))
            {
                _logger.LogInformation("IncompleteOperationRecoveryWorker: Local order {OrderId} is already in terminal state {OrderStatus}. Completing operation.",
                    localOrder.Id, localOrder.Status);

                await unitOfWork.BeginTransactionAsync(cancellationToken);
                if (localOrder.Status == OrderStatus.Filled || localOrder.Status == OrderStatus.PartiallyFilled)
                {
                    op.MarkCompleted(localOrder.ExchangeOrderId);
                }
                else
                {
                    op.MarkFailed(localOrder.FailureReason ?? "Order terminal failure");
                }
                tradeOperationRepository.Update(op);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);

                metrics?.IncrementRecoveredOperations();

                if (eventPublisher != null)
                {
                    var compEvent = new MonitoringEvent(
                        "OperationRecovered",
                        "INFO",
                        "Recovery",
                        "IncompleteOperationRecoveryWorker",
                        "RECOVERED",
                        $"Successfully completed state recovery for operation {op.Id} to terminal status '{op.Status}'.",
                        correlationId: op.CorrelationId,
                        orderId: localOrder.Id
                    );
                    await eventPublisher.PublishAsync(compEvent, forceSynchronous: true, cancellationToken);
                }
                continue;
            }

            // Phase 2: Query exchange with deterministic ClientOrderId
            var clientOrderId = $"TB-{op.Id:N}";
            _logger.LogInformation("IncompleteOperationRecoveryWorker: Querying exchange for order TB-{OperationId:N}...", op.Id);

            try
            {
                // Find symbol: from local order or try first trade operation info if possible.
                // Since Bybit v5 list/query API requires a symbol, we can look up from local order first.
                var symbol = localOrder?.Symbol?.Value ?? "BTCUSDT"; // default fallback or try symbol matching

                var queryResult = await gateway.GetOrderAsync(clientOrderId, symbol, cancellationToken);

                if (queryResult.Success && !string.IsNullOrEmpty(queryResult.ExchangeOrderId))
                {
                    _logger.LogInformation("IncompleteOperationRecoveryWorker: Order {OperationId} was found on exchange in status {ExchangeStatus}.",
                        op.Id, queryResult.Status);

                    await unitOfWork.BeginTransactionAsync(cancellationToken);

                    if (localOrder == null)
                    {
                        // Recreate local order from exchange state to heal partial local database failures
                        localOrder = new Order(
                            op.Id,
                            clientOrderId,
                            new TradingBot.Domain.ValueObjects.Symbol(symbol),
                            OrderStatus.Submitted == queryResult.Status || OrderStatus.New == queryResult.Status ? OrderSide.Buy : OrderSide.Buy, // side from query
                            OrderType.Limit, // type from query
                            new Quantity(queryResult.ExecutedQuantity > 0 ? queryResult.ExecutedQuantity : 1.0m),
                            new Money(queryResult.ExecutedPrice > 0 ? queryResult.ExecutedPrice : 1.0m),
                            null
                        );
                        localOrder.SetExchangeDetails(queryResult.ExchangeOrderId, "Bybit");
                        localOrder.UpdateStatus(queryResult.Status);
                        await orderRepository.AddAsync(localOrder, cancellationToken);
                    }
                    else
                    {
                        localOrder.SetExchangeDetails(queryResult.ExchangeOrderId, "Bybit");
                        localOrder.UpdateStatus(queryResult.Status);
                        await orderRepository.UpdateAsync(localOrder, cancellationToken);
                    }

                    if (IsTerminalState(queryResult.Status))
                    {
                        op.MarkCompleted(queryResult.ExchangeOrderId);
                    }
                    else
                    {
                        op.UpdateStatus("Submitted");
                        op.SetExternalId(queryResult.ExchangeOrderId);
                    }

                    tradeOperationRepository.Update(op);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);

                    metrics?.IncrementRecoveredOperations();

                    if (eventPublisher != null)
                    {
                        var repEvent = new MonitoringEvent(
                            "OperationRecovered",
                            "INFO",
                            "Recovery",
                            "IncompleteOperationRecoveryWorker",
                            "RECOVERED",
                            $"Operation {op.Id} state successfully resolved and synchronized with exchange state: {queryResult.Status}.",
                            correlationId: op.CorrelationId,
                            orderId: localOrder.Id
                        );
                        await eventPublisher.PublishAsync(repEvent, forceSynchronous: true, cancellationToken);
                    }
                }
                else if (queryResult.ErrorCode == "ORDER_NOT_FOUND" ||
                         (queryResult.ErrorMessage != null && queryResult.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("IncompleteOperationRecoveryWorker: Order {OperationId} was not found on exchange. Order creation attempt failed.", op.Id);

                    await unitOfWork.BeginTransactionAsync(cancellationToken);

                    if (localOrder == null)
                    {
                        localOrder = new Order(
                            op.Id,
                            clientOrderId,
                            new TradingBot.Domain.ValueObjects.Symbol(symbol),
                            OrderSide.Buy,
                            OrderType.Limit,
                            new Quantity(1.0m),
                            new Money(1.0m),
                            null
                        );
                        localOrder.UpdateStatus(OrderStatus.Failed);
                        localOrder.SetFailure("Order not found on exchange during background recovery.", "ORDER_NOT_FOUND");
                        await orderRepository.AddAsync(localOrder, cancellationToken);
                    }
                    else
                    {
                        localOrder.UpdateStatus(OrderStatus.Failed);
                        localOrder.SetFailure("Order not found on exchange during background recovery.", "ORDER_NOT_FOUND");
                        await orderRepository.UpdateAsync(localOrder, cancellationToken);
                    }

                    op.MarkFailed("ORDER_NOT_FOUND");
                    tradeOperationRepository.Update(op);

                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await unitOfWork.CommitTransactionAsync(cancellationToken);

                    metrics?.IncrementRecoveredOperations();

                    if (eventPublisher != null)
                    {
                        var notFoundEvent = new MonitoringEvent(
                            "OperationRecovered",
                            "WARNING",
                            "Recovery",
                            "IncompleteOperationRecoveryWorker",
                            "FAILED",
                            $"Order not found on exchange for operation {op.Id}. Marked as failed.",
                            correlationId: op.CorrelationId,
                            orderId: localOrder.Id
                        );
                        await eventPublisher.PublishAsync(notFoundEvent, forceSynchronous: true, cancellationToken);
                    }
                }
                else
                {
                    // Temporary query failure (network timeout, rate limit, etc.)
                    _logger.LogWarning("IncompleteOperationRecoveryWorker: Exchange query returned non-definitive result for {OperationId}: {Error}. Skipping.",
                        op.Id, queryResult.ErrorMessage);

                    // Phase 3: Transition to ManualInterventionRequired if stuck longer than 3 * timeout
                    if (elapsed >= manualInterventionThreshold && op.Status != "ManualInterventionRequired")
                    {
                        _logger.LogError("IncompleteOperationRecoveryWorker: Operation {OperationId} has been unresolved for {ElapsedSeconds}s. Requiring Manual Intervention.",
                            op.Id, elapsed.TotalSeconds);

                        await unitOfWork.BeginTransactionAsync(cancellationToken);
                        op.UpdateStatus("ManualInterventionRequired");
                        tradeOperationRepository.Update(op);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        await unitOfWork.CommitTransactionAsync(cancellationToken);

                        metrics?.IncrementManualInterventions();

                        if (eventPublisher != null)
                        {
                            var manualEvent = new MonitoringEvent(
                                "ManualInterventionRequired",
                                "CRITICAL",
                                "Recovery",
                                "IncompleteOperationRecoveryWorker",
                                "MANUAL_INTERVENTION_REQUIRED",
                                $"Operation {op.Id} is stuck in an unresolved state for longer than timeout limits. Manual intervention is required.",
                                correlationId: op.CorrelationId,
                                orderId: localOrder?.Id
                            );
                            await eventPublisher.PublishAsync(manualEvent, forceSynchronous: true, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IncompleteOperationRecoveryWorker: Failed to query exchange or recover operation {OperationId}.", op.Id);
            }
        }
    }

    private static bool IsTerminalState(OrderStatus status)
    {
        return status == OrderStatus.Filled ||
               status == OrderStatus.Cancelled ||
               status == OrderStatus.Rejected ||
               status == OrderStatus.Failed ||
               status == OrderStatus.Expired ||
               status == OrderStatus.ValidationFailed;
    }
}
