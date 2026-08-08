using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Enums;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class OrderReconciliationService : IOrderReconciliationService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderEventRepository _orderEventRepository;
    private readonly IExchangeTradingGateway _gateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OrderReconciliationService> _logger;
    private readonly IExecutionMetrics? _metrics;

    public static DateTime LastRunTime { get; private set; } = DateTime.MinValue;

    public OrderReconciliationService(
        IOrderRepository orderRepository,
        IOrderEventRepository orderEventRepository,
        IExchangeTradingGateway gateway,
        IUnitOfWork unitOfWork,
        ILogger<OrderReconciliationService> logger,
        IExecutionMetrics? metrics = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        LastRunTime = DateTime.UtcNow;
        _logger.LogInformation("OrderReconciliationStarted: Loading active orders requiring reconciliation...");

        var overallStopwatch = Stopwatch.StartNew();

        // Load bounded batch size of 50
        var orders = await _orderRepository.GetPendingReconciliationOrdersAsync(50, cancellationToken);

        int processedCount = 0;
        foreach (var order in orders)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("OrderReconciliationCancelled: Reconciliation cancelled by token.");
                break;
            }

            _logger.LogInformation("OrderReconciliationProgress: Reconciling local order {OrderId}, ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, LocalStatus={LocalStatus}",
                order.Id, order.ClientOrderId, order.ExchangeOrderId, order.Status);

            try
            {
                // Isolate each order reconciliation in its own transaction context
                await ReconcileSingleOrderAsync(order, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderReconciliationFailed: Error occurred while reconciling order {OrderId}. Continuing to next order in batch.", order.Id);
            }
        }

        overallStopwatch.Stop();
        _metrics?.RecordReconciliation(overallStopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation("OrderReconciliationCompleted: Reconciled {ProcessedCount} orders in this pass.", processedCount);
    }

    private async Task ReconcileSingleOrderAsync(Order order, CancellationToken cancellationToken)
    {
        // Query the exchange using either ExchangeOrderId or ClientOrderId (smart query)
        var gatewayStopwatch = Stopwatch.StartNew();
        OrderResult queryResult;

        try
        {
            queryResult = await _gateway.GetOrderAsync(order.ExchangeOrderId ?? order.ClientOrderId, order.Symbol.Value, cancellationToken);
            gatewayStopwatch.Stop();
            _metrics?.RecordExchangeCall(gatewayStopwatch.Elapsed.TotalMilliseconds, isError: !queryResult.Success, isRateLimit: false, isTimeout: false);
        }
        catch (Exception ex)
        {
            gatewayStopwatch.Stop();
            _metrics?.RecordExchangeCall(gatewayStopwatch.Elapsed.TotalMilliseconds, isError: true, isRateLimit: false, isTimeout: ex is TimeoutException);
            throw;
        }

        var dbStopwatch = Stopwatch.StartNew();
        bool dbSuccess = false;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var previousStatus = order.Status;

            if (!queryResult.Success)
            {
                // If the exchange query failed because the order was not found
                if (queryResult.ErrorCode == "ORDER_NOT_FOUND" || queryResult.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) || queryResult.ErrorType == ExchangeErrorType.InvalidRequest)
                {
                    _logger.LogWarning("OrderReconciliationNotFound: Order {OrderId} was not found on exchange. Marking local order as Failed.", order.Id);

                    order.UpdateStatus(OrderStatus.Failed);
                    order.SetFailure("Order not found on exchange during reconciliation query.", "ORDER_NOT_FOUND");
                    await _orderRepository.UpdateAsync(order, cancellationToken);

                    var notFoundEvent = new OrderEvent(
                        order.Id,
                        previousStatus,
                        OrderStatus.Failed,
                        "OrderFailed",
                        "OrderReconciliationService",
                        "Order was not found on the exchange during reconciliation. Marked as Failed.");
                    await _orderEventRepository.AddAsync(notFoundEvent, cancellationToken);

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);
                    dbSuccess = true;
                }
                else
                {
                    // Query failed due to some temporary issue (e.g., connection timed out). No changes made to database.
                    _logger.LogWarning("OrderReconciliationQueryFailed: Temporary query error for order {OrderId}. ErrorMsg={ErrorMsg}. Skipping in this pass.", order.Id, queryResult.ErrorMessage);
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                }
                return;
            }

            // Successfully queried exchange order state
            var exchangeStatus = queryResult.Status;

            // Link ExchangeOrderId if it was missing locally (recovery flow)
            if (string.IsNullOrEmpty(order.ExchangeOrderId) && !string.IsNullOrEmpty(queryResult.ExchangeOrderId))
            {
                _logger.LogInformation("OrderReconciliationRecovered: Recovered missing ExchangeOrderId={ExchangeOrderId} for local order {OrderId}",
                    queryResult.ExchangeOrderId, order.Id);
                order.SetExchangeDetails(queryResult.ExchangeOrderId, "Bybit");
            }

            if (order.Status == exchangeStatus)
            {
                // Local status matches exchange. Just update executions if needed and return.
                if (queryResult.ExecutedQuantity > 0 && queryResult.ExecutedQuantity != order.ExecutedQuantity)
                {
                    _logger.LogInformation("OrderReconciliationExecutionsUpdated: Local order {OrderId} execution quantity updated to {ExecQty}, Price {ExecPrice}",
                        order.Id, queryResult.ExecutedQuantity, queryResult.ExecutedPrice);
                    order.RecordExecution(queryResult.ExecutedQuantity - order.ExecutedQuantity, queryResult.ExecutedPrice);
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                dbSuccess = true;
                return;
            }

            // Status is different. Verify and apply transition.
            bool isDowngrade = IsDowngradeTransition(order.Status, exchangeStatus);
            if (isDowngrade)
            {
                _logger.LogWarning("OrderStateConflict: Local status is {LocalStatus} but exchange status is {ExchangeStatus} for order {OrderId}. This represents an invalid downgrade. Skipping status update.",
                    order.Status, exchangeStatus, order.Id);

                var conflictEvent = new OrderEvent(
                    order.Id,
                    order.Status,
                    exchangeStatus,
                    "OrderStateConflict",
                    "OrderReconciliationService",
                    $"State conflict: Local status is {order.Status} but exchange reports {exchangeStatus}. Downgrade rejected.");
                await _orderEventRepository.AddAsync(conflictEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                dbSuccess = true;
                return;
            }

            // Valid progressive transition
            _logger.LogInformation("OrderReconciled: Reconciled order {OrderId} status from {LocalStatus} to {ExchangeStatus}",
                order.Id, order.Status, exchangeStatus);

            // Update status and details
            order.UpdateStatus(exchangeStatus);

            if (queryResult.ExecutedQuantity > 0)
            {
                decimal diffQty = queryResult.ExecutedQuantity - order.ExecutedQuantity;
                if (diffQty > 0)
                {
                    order.RecordExecution(diffQty, queryResult.ExecutedPrice);
                }
            }
            else if (exchangeStatus == OrderStatus.Filled)
            {
                order.MarkFilled();
            }
            else if (exchangeStatus == OrderStatus.PartiallyFilled)
            {
                order.MarkPartiallyFilled();
            }
            else if (exchangeStatus == OrderStatus.Cancelled)
            {
                order.Cancel();
            }

            await _orderRepository.UpdateAsync(order, cancellationToken);

            var reconciledEvent = new OrderEvent(
                order.Id,
                previousStatus,
                order.Status,
                "OrderReconciled",
                "OrderReconciliationService",
                $"Reconciled local state with exchange. ExchangeStatus={exchangeStatus}, ExecQty={queryResult.ExecutedQuantity}, AvgPrice={queryResult.ExecutedPrice}");
            await _orderEventRepository.AddAsync(reconciledEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            dbSuccess = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TransactionRollback: Failed to persist reconciliation updates for order {OrderId}. Rolling back transaction.", order.Id);
            try { await _unitOfWork.RollbackTransactionAsync(cancellationToken); } catch { }
            throw;
        }
        finally
        {
            dbStopwatch.Stop();
            _metrics?.RecordDatabasePersistence(dbStopwatch.Elapsed.TotalMilliseconds, dbSuccess);
        }
    }

    private static bool IsDowngradeTransition(OrderStatus localStatus, OrderStatus exchangeStatus)
    {
        if (localStatus == exchangeStatus) return false;

        if (IsTerminalState(localStatus))
        {
            return true;
        }

        if (IsTerminalState(exchangeStatus))
        {
            return false;
        }

        int localWeight = GetStatusWeight(localStatus);
        int exchangeWeight = GetStatusWeight(exchangeStatus);

        return exchangeWeight < localWeight;
    }

    private static int GetStatusWeight(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Created => 0,
            OrderStatus.Pending => 1,
            OrderStatus.ValidationFailed => 1,
            OrderStatus.ReadyForExchange => 2,
            OrderStatus.Submitting => 3,
            OrderStatus.Submitted => 4,
            OrderStatus.Accepted => 5,
            OrderStatus.New => 6,
            OrderStatus.PartiallyFilled => 7,
            OrderStatus.Unknown => 1,
            _ => 0
        };
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
