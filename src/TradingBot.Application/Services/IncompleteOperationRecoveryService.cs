using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;

namespace TradingBot.Application.Services;

public class IncompleteOperationRecoveryService : IIncompleteOperationRecoveryService
{
    private readonly ITradeOperationRepository _tradeOperationRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly TradingBot.Application.Trading.Execution.Contracts.IExchangeTradingGateway _gateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IncompleteOperationRecoveryService> _logger;
    private readonly IdempotencyOptions _options;
    private readonly IMetricsService? _metrics;
    private readonly IMonitoringEventPublisher? _eventPublisher;

    public IncompleteOperationRecoveryService(
        ITradeOperationRepository tradeOperationRepository,
        IOrderRepository orderRepository,
        TradingBot.Application.Trading.Execution.Contracts.IExchangeTradingGateway gateway,
        IUnitOfWork unitOfWork,
        IdempotencyOptions options,
        ILogger<IncompleteOperationRecoveryService> logger,
        IMetricsService? metrics = null,
        IMonitoringEventPublisher? eventPublisher = null)
    {
        _tradeOperationRepository = tradeOperationRepository ?? throw new ArgumentNullException(nameof(tradeOperationRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _eventPublisher = eventPublisher;
    }

    public async Task RecoverIncompleteOperationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IncompleteOperationRecoveryService: Starting recovery pass...");

        // Load all non-terminal operations from DB
        var operations = await _tradeOperationRepository.GetAllAsync(cancellationToken);
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

            _logger.LogInformation("IncompleteOperationRecoveryService: Processing recovery candidate operation {OperationId} in status {Status}. Elapsed={ElapsedSeconds}s",
                op.Id, op.Status, elapsed.TotalSeconds);

            if (_eventPublisher != null)
            {
                var startedEvent = new MonitoringEvent(
                    "OperationRecoveryStarted",
                    "INFO",
                    "Recovery",
                    "IncompleteOperationRecoveryService",
                    "STARTED",
                    $"Started background state recovery for operation {op.Id} (Status={op.Status}).",
                    correlationId: op.CorrelationId
                );
                await _eventPublisher.PublishAsync(startedEvent, forceSynchronous: true, cancellationToken);
            }

            // Phase 1: Check if local Order status is already terminal
            var localOrder = await _orderRepository.GetByIdAsync(op.Id, cancellationToken);
            if (localOrder != null && IsTerminalState(localOrder.Status))
            {
                _logger.LogInformation("IncompleteOperationRecoveryService: Local order {OrderId} is already in terminal state {OrderStatus}. Completing operation.",
                    localOrder.Id, localOrder.Status);

                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                if (localOrder.Status == OrderStatus.Filled || localOrder.Status == OrderStatus.PartiallyFilled)
                {
                    op.MarkCompleted(localOrder.ExchangeOrderId);
                }
                else
                {
                    op.MarkFailed(localOrder.FailureReason ?? "Order terminal failure");
                }
                _tradeOperationRepository.Update(op);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _metrics?.IncrementRecoveredOperations();

                if (_eventPublisher != null)
                {
                    var compEvent = new MonitoringEvent(
                        "OperationRecovered",
                        "INFO",
                        "Recovery",
                        "IncompleteOperationRecoveryService",
                        "RECOVERED",
                        $"Successfully completed state recovery for operation {op.Id} to terminal status '{op.Status}'.",
                        correlationId: op.CorrelationId,
                        orderId: localOrder.Id
                    );
                    await _eventPublisher.PublishAsync(compEvent, forceSynchronous: true, cancellationToken);
                }
                continue;
            }

            // Phase 2: Query exchange with deterministic ClientOrderId
            var clientOrderId = $"TB-{op.Id:N}";
            _logger.LogInformation("IncompleteOperationRecoveryService: Querying exchange for order TB-{OperationId:N}...", op.Id);

            try
            {
                var symbol = localOrder?.Symbol?.Value ?? "BTCUSDT";

                var queryResult = await _gateway.GetOrderAsync(clientOrderId, symbol, cancellationToken);

                if (queryResult.Success && !string.IsNullOrEmpty(queryResult.ExchangeOrderId))
                {
                    _logger.LogInformation("IncompleteOperationRecoveryService: Order {OperationId} was found on exchange in status {ExchangeStatus}.",
                        op.Id, queryResult.Status);

                    await _unitOfWork.BeginTransactionAsync(cancellationToken);

                    if (localOrder == null)
                    {
                        localOrder = new Order(
                            op.Id,
                            clientOrderId,
                            new TradingBot.Domain.ValueObjects.Symbol(symbol),
                            OrderStatus.Submitted == queryResult.Status || OrderStatus.New == queryResult.Status ? OrderSide.Buy : OrderSide.Buy,
                            OrderType.Limit,
                            new Quantity(queryResult.ExecutedQuantity > 0 ? queryResult.ExecutedQuantity : 1.0m),
                            new Money(queryResult.ExecutedPrice > 0 ? queryResult.ExecutedPrice : 1.0m),
                            null
                        );
                        localOrder.SetExchangeDetails(queryResult.ExchangeOrderId, "Bybit");
                        localOrder.UpdateStatus(queryResult.Status);
                        await _orderRepository.AddAsync(localOrder, cancellationToken);
                    }
                    else
                    {
                        localOrder.SetExchangeDetails(queryResult.ExchangeOrderId, "Bybit");
                        localOrder.UpdateStatus(queryResult.Status);
                        await _orderRepository.UpdateAsync(localOrder, cancellationToken);
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

                    _tradeOperationRepository.Update(op);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);

                    _metrics?.IncrementRecoveredOperations();

                    if (_eventPublisher != null)
                    {
                        var repEvent = new MonitoringEvent(
                            "OperationRecovered",
                            "INFO",
                            "Recovery",
                            "IncompleteOperationRecoveryService",
                            "RECOVERED",
                            $"Operation {op.Id} state successfully resolved and synchronized with exchange state: {queryResult.Status}.",
                            correlationId: op.CorrelationId,
                            orderId: localOrder.Id
                        );
                        await _eventPublisher.PublishAsync(repEvent, forceSynchronous: true, cancellationToken);
                    }
                }
                else if (queryResult.ErrorCode == "ORDER_NOT_FOUND" ||
                         (queryResult.ErrorMessage != null && queryResult.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("IncompleteOperationRecoveryService: Order {OperationId} was not found on exchange. Order creation attempt failed.", op.Id);

                    await _unitOfWork.BeginTransactionAsync(cancellationToken);

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
                        await _orderRepository.AddAsync(localOrder, cancellationToken);
                    }
                    else
                    {
                        localOrder.UpdateStatus(OrderStatus.Failed);
                        localOrder.SetFailure("Order not found on exchange during background recovery.", "ORDER_NOT_FOUND");
                        await _orderRepository.UpdateAsync(localOrder, cancellationToken);
                    }

                    op.MarkFailed("ORDER_NOT_FOUND");
                    _tradeOperationRepository.Update(op);

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);

                    _metrics?.IncrementRecoveredOperations();

                    if (_eventPublisher != null)
                    {
                        var notFoundEvent = new MonitoringEvent(
                            "OperationRecovered",
                            "WARNING",
                            "Recovery",
                            "IncompleteOperationRecoveryService",
                            "FAILED",
                            $"Order not found on exchange for operation {op.Id}. Marked as failed.",
                            correlationId: op.CorrelationId,
                            orderId: localOrder.Id
                        );
                        await _eventPublisher.PublishAsync(notFoundEvent, forceSynchronous: true, cancellationToken);
                    }
                }
                else
                {
                    _logger.LogWarning("IncompleteOperationRecoveryService: Exchange query returned non-definitive result for {OperationId}: {Error}. Skipping.",
                        op.Id, queryResult.ErrorMessage);

                    // Phase 3: Transition to ManualInterventionRequired if stuck longer than 3 * timeout
                    if (elapsed >= manualInterventionThreshold && op.Status != "ManualInterventionRequired")
                    {
                        _logger.LogError("IncompleteOperationRecoveryService: Operation {OperationId} has been unresolved for {ElapsedSeconds}s. Requiring Manual Intervention.",
                            op.Id, elapsed.TotalSeconds);

                        await _unitOfWork.BeginTransactionAsync(cancellationToken);
                        op.UpdateStatus("ManualInterventionRequired");
                        _tradeOperationRepository.Update(op);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        await _unitOfWork.CommitTransactionAsync(cancellationToken);

                        _metrics?.IncrementManualInterventions();

                        if (_eventPublisher != null)
                        {
                            var manualEvent = new MonitoringEvent(
                                "ManualInterventionRequired",
                                "CRITICAL",
                                "Recovery",
                                "IncompleteOperationRecoveryService",
                                "MANUAL_INTERVENTION_REQUIRED",
                                $"Operation {op.Id} is stuck in an unresolved state for longer than timeout limits. Manual intervention is required.",
                                correlationId: op.CorrelationId,
                                orderId: localOrder?.Id
                            );
                            await _eventPublisher.PublishAsync(manualEvent, forceSynchronous: true, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IncompleteOperationRecoveryService: Failed to query exchange or recover operation {OperationId}.", op.Id);
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
