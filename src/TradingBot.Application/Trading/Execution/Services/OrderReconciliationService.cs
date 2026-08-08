using System;
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

    public OrderReconciliationService(
        IOrderRepository orderRepository,
        IOrderEventRepository orderEventRepository,
        IExchangeTradingGateway gateway,
        IUnitOfWork unitOfWork,
        ILogger<OrderReconciliationService> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("OrderReconciliationStarted: Loading active orders requiring reconciliation...");

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

        _logger.LogInformation("OrderReconciliationCompleted: Reconciled {ProcessedCount} orders in this pass.", processedCount);
    }

    private async Task ReconcileSingleOrderAsync(Order order, CancellationToken cancellationToken)
    {
        // Query the exchange using either ExchangeOrderId or ClientOrderId (smart query)
        var queryId = order.ExchangeOrderId ?? order.ClientOrderId;
        var queryResult = await _gateway.GetOrderAsync(queryId, order.Symbol.Value, cancellationToken);

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
                return;
            }

            // Status is different. Verify and apply transition.
            // Check state downgrade (Section 16: Local Filled, Exchange New is invalid)
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
                return;
            }

            // Valid progressive transition
            _logger.LogInformation("OrderReconciled: Reconciled order {OrderId} status from {LocalStatus} to {ExchangeStatus}",
                order.Id, order.Status, exchangeStatus);

            // Update status and details
            order.UpdateStatus(exchangeStatus);

            if (queryResult.ExecutedQuantity > 0)
            {
                // Reset executed quantites first to overwrite cleanly, or calculate differences
                // Since RecordExecution accumulates, we can adjust ExecutedQuantity or record execution of the entire filled amount directly
                decimal diffQty = queryResult.ExecutedQuantity - order.ExecutedQuantity;
                if (diffQty > 0)
                {
                    order.RecordExecution(diffQty, queryResult.ExecutedPrice);
                }
            }
            else if (exchangeStatus == OrderStatus.Filled)
            {
                // Fallback fill to complete requested quantity if ExecutedQuantity is not set
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TransactionRollback: Failed to persist reconciliation updates for order {OrderId}. Rolling back transaction.", order.Id);
            try { await _unitOfWork.RollbackTransactionAsync(cancellationToken); } catch { }
            throw;
        }
    }

    private static bool IsDowngradeTransition(OrderStatus localStatus, OrderStatus exchangeStatus)
    {
        if (localStatus == exchangeStatus) return false;

        // If local status is already terminal, moving back to non-terminal or any other state is a downgrade
        if (IsTerminalState(localStatus))
        {
            return true;
        }

        // If exchange status is terminal, and local is not terminal, it is always a progressive upgrade
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
            OrderStatus.Unknown => 1, // Unknown is low weight to allow recovery to New/Filled/etc.
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
