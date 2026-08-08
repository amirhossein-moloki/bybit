using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Events;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class TradeExecutionOrchestrator : ITradeExecutionOrchestrator
{
    private readonly IOrderValidator _validator;
    private readonly IOrderRepository _orderRepository;
    private readonly ITradeExecutionService _executionService;
    private readonly IExecutionEventPublisher _eventPublisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TradeExecutionOrchestrator> _logger;

    public TradeExecutionOrchestrator(
        IOrderValidator validator,
        IOrderRepository orderRepository,
        ITradeExecutionService executionService,
        IExecutionEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        ILogger<TradeExecutionOrchestrator> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TradeExecutionResult> OrchestrateAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var overallStopwatch = Stopwatch.StartNew();

        // 1. Publish: TradeExecutionStarted (Section 6 & 9)
        _logger.LogInformation("TradeExecutionStarted: Starting execution orchestrator. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
            request.Id, request.SignalId, request.Symbol, OrderStatus.Created, 0.0);

        await _eventPublisher.PublishAsync(new TradeExecutionStartedEvent(
            request.Id,
            request.SignalId,
            request.Symbol,
            OrderStatus.Created,
            TimeSpan.Zero,
            DateTime.UtcNow
        ), cancellationToken);

        // 2. Validate Risk Decision (Section 9)
        _logger.LogInformation("RiskApprovalReceived: Validating risk approval state. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
            request.Id, request.SignalId, request.Symbol, OrderStatus.Created, overallStopwatch.Elapsed.TotalMilliseconds);

        if (request.RiskDecision != RiskDecisionStatus.Approved)
        {
            var rejectReason = $"Risk approval boundary violated: Decision is {request.RiskDecision}.";
            _logger.LogWarning("ExecutionFailed: {Reason}. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                rejectReason, request.Id, request.SignalId, request.Symbol, OrderStatus.ValidationFailed, overallStopwatch.Elapsed.TotalMilliseconds);

            await _eventPublisher.PublishAsync(new OrderRejectedEvent(
                request.Id,
                null,
                request.SignalId,
                request.Symbol,
                OrderStatus.ValidationFailed,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                rejectReason
            ), cancellationToken);

            await _eventPublisher.PublishAsync(new TradeExecutionCompletedEvent(
                request.Id,
                null,
                request.SignalId,
                request.Symbol,
                OrderStatus.ValidationFailed,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                false
            ), cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new TradeExecutionResult
            {
                Success = false,
                Status = OrderStatus.ValidationFailed,
                FailureReason = rejectReason,
                ExecutedPrice = 0,
                ExecutedQuantity = 0
            };
        }

        // 3. Early Validate Execution Request (Section 12)
        try
        {
            _validator.Validate(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionFailed: Validation failed. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                request.Id, request.SignalId, request.Symbol, OrderStatus.ValidationFailed, overallStopwatch.Elapsed.TotalMilliseconds);

            await _eventPublisher.PublishAsync(new OrderRejectedEvent(
                request.Id,
                null,
                request.SignalId,
                request.Symbol,
                OrderStatus.ValidationFailed,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                ex.Message
            ), cancellationToken);

            await _eventPublisher.PublishAsync(new TradeExecutionCompletedEvent(
                request.Id,
                null,
                request.SignalId,
                request.Symbol,
                OrderStatus.ValidationFailed,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                false
            ), cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new TradeExecutionResult
            {
                Success = false,
                Status = OrderStatus.ValidationFailed,
                FailureReason = ex.Message,
                ExecutedPrice = 0,
                ExecutedQuantity = 0
            };
        }

        // 4. Check Duplicate Execution (Section 4 & 11)
        _logger.LogInformation("OrderCreationStarted: Checking duplicate execution in local persistence. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
            request.Id, request.SignalId, request.Symbol, OrderStatus.Pending, overallStopwatch.Elapsed.TotalMilliseconds);

        var existingOrder = await _orderRepository.GetBySignalIdAsync(request.SignalId, cancellationToken);
        if (existingOrder != null)
        {
            _logger.LogInformation("ExecutionCompleted: Duplicate execution detected. Returning existing order details. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                request.Id, request.SignalId, request.Symbol, existingOrder.Status, overallStopwatch.Elapsed.TotalMilliseconds);

            bool success = existingOrder.Status == OrderStatus.Filled ||
                           existingOrder.Status == OrderStatus.PartiallyFilled ||
                           existingOrder.Status == OrderStatus.Accepted ||
                           existingOrder.Status == OrderStatus.New ||
                           existingOrder.Status == OrderStatus.Submitted;

            await _eventPublisher.PublishAsync(new TradeExecutionCompletedEvent(
                request.Id,
                existingOrder.Id,
                request.SignalId,
                request.Symbol,
                existingOrder.Status,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                success
            ), cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new TradeExecutionResult
            {
                Success = success,
                OrderId = existingOrder.Id,
                ExchangeOrderId = existingOrder.ExchangeOrderId,
                Status = existingOrder.Status,
                ExecutedPrice = existingOrder.ExecutedPrice,
                ExecutedQuantity = existingOrder.ExecutedQuantity,
                FailureReason = $"Duplicate request detected. Found existing order in status {existingOrder.Status}."
            };
        }

        // 5. Submit & Execute Order (Section 4 & 9)
        _logger.LogInformation("ExchangeRequestSent: Submitting order request to execution service and exchange. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
            request.Id, request.SignalId, request.Symbol, OrderStatus.Submitting, overallStopwatch.Elapsed.TotalMilliseconds);

        await _eventPublisher.PublishAsync(new OrderSubmissionStartedEvent(
            request.Id,
            null,
            request.SignalId,
            request.Symbol,
            OrderStatus.Submitting,
            overallStopwatch.Elapsed,
            DateTime.UtcNow
        ), cancellationToken);

        ExecutionResult executionResult;
        try
        {
            executionResult = await _executionService.ExecuteAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecutionFailed: Order execution crashed with exception. ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                request.Id, request.SignalId, request.Symbol, OrderStatus.Failed, overallStopwatch.Elapsed.TotalMilliseconds);

            await _eventPublisher.PublishAsync(new OrderFailedEvent(
                request.Id,
                null,
                request.SignalId,
                request.Symbol,
                OrderStatus.Failed,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                ex.Message
            ), cancellationToken);

            await _eventPublisher.PublishAsync(new TradeExecutionCompletedEvent(
                request.Id,
                null,
                request.SignalId,
                request.Symbol,
                OrderStatus.Failed,
                overallStopwatch.Elapsed,
                DateTime.UtcNow,
                false
            ), cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new TradeExecutionResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                FailureReason = ex.Message,
                ExecutedPrice = 0,
                ExecutedQuantity = 0
            };
        }

        _logger.LogInformation("ExchangeResponseReceived: Received exchange execution result. Success: {Success}, Status: {Status}, ExecutionId: {ExecutionId}, SignalId: {SignalId}, Symbol: {Symbol}, Duration: {DurationMs}ms",
            executionResult.Success, executionResult.Status, request.Id, request.SignalId, request.Symbol, overallStopwatch.Elapsed.TotalMilliseconds);

        // Fetch the newly created/persisted local order to retrieve exact filled quantities if possible
        var localOrder = await _orderRepository.GetBySignalIdAsync(request.SignalId, cancellationToken);
        var orderId = localOrder?.Id ?? executionResult.OrderId;
        var exchangeOrderId = localOrder?.ExchangeOrderId ?? executionResult.ExchangeOrderId;
        var executedPrice = localOrder?.ExecutedPrice ?? 0m;
        var executedQty = localOrder?.ExecutedQuantity ?? 0m;

        // 6. Track and Publish Specific Events (Section 4 & 6)
        if (executionResult.Success)
        {
            if (executionResult.Status == OrderStatus.Filled)
            {
                _logger.LogInformation("OrderFilled: Order filled completely on exchange. ExecutionId: {ExecutionId}, OrderId: {OrderId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                    request.Id, orderId, request.SignalId, request.Symbol, OrderStatus.Filled, overallStopwatch.Elapsed.TotalMilliseconds);

                await _eventPublisher.PublishAsync(new OrderFilledEvent(
                    request.Id,
                    orderId,
                    request.SignalId,
                    request.Symbol,
                    OrderStatus.Filled,
                    overallStopwatch.Elapsed,
                    DateTime.UtcNow,
                    executedPrice,
                    executedQty
                ), cancellationToken);
            }
            else
            {
                _logger.LogInformation("OrderSubmitted: Order submitted and accepted/new on exchange. ExecutionId: {ExecutionId}, OrderId: {OrderId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                    request.Id, orderId, request.SignalId, request.Symbol, executionResult.Status, overallStopwatch.Elapsed.TotalMilliseconds);

                await _eventPublisher.PublishAsync(new OrderSubmittedEvent(
                    request.Id,
                    orderId,
                    request.SignalId,
                    request.Symbol,
                    executionResult.Status,
                    overallStopwatch.Elapsed,
                    DateTime.UtcNow
                ), cancellationToken);
            }
        }
        else
        {
            if (executionResult.Status == OrderStatus.Rejected)
            {
                _logger.LogWarning("ExecutionFailed: Order rejected by exchange or validator. ExecutionId: {ExecutionId}, OrderId: {OrderId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                    request.Id, orderId, request.SignalId, request.Symbol, OrderStatus.Rejected, overallStopwatch.Elapsed.TotalMilliseconds);

                await _eventPublisher.PublishAsync(new OrderRejectedEvent(
                    request.Id,
                    orderId,
                    request.SignalId,
                    request.Symbol,
                    OrderStatus.Rejected,
                    overallStopwatch.Elapsed,
                    DateTime.UtcNow,
                    executionResult.Message
                ), cancellationToken);
            }
            else
            {
                _logger.LogError("ExecutionFailed: Order failed or timed out during execution. ExecutionId: {ExecutionId}, OrderId: {OrderId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
                    request.Id, orderId, request.SignalId, request.Symbol, executionResult.Status, overallStopwatch.Elapsed.TotalMilliseconds);

                await _eventPublisher.PublishAsync(new OrderFailedEvent(
                    request.Id,
                    orderId,
                    request.SignalId,
                    request.Symbol,
                    executionResult.Status,
                    overallStopwatch.Elapsed,
                    DateTime.UtcNow,
                    executionResult.Message
                ), cancellationToken);
            }
        }

        // 7. Complete: TradeExecutionCompleted
        _logger.LogInformation("ExecutionCompleted: Finished orchestrator execution pipeline. Success: {Success}, ExecutionId: {ExecutionId}, OrderId: {OrderId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
            executionResult.Success, request.Id, orderId, request.SignalId, request.Symbol, executionResult.Status, overallStopwatch.Elapsed.TotalMilliseconds);

        await _eventPublisher.PublishAsync(new TradeExecutionCompletedEvent(
            request.Id,
            orderId,
            request.SignalId,
            request.Symbol,
            executionResult.Status,
            overallStopwatch.Elapsed,
            DateTime.UtcNow,
            executionResult.Success
        ), cancellationToken);

        // Save and commit all published events/logs to persistence
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new TradeExecutionResult
        {
            Success = executionResult.Success,
            OrderId = orderId,
            ExchangeOrderId = exchangeOrderId,
            Status = executionResult.Status,
            FailureReason = executionResult.Success ? null : executionResult.Message,
            ExecutedPrice = executedPrice,
            ExecutedQuantity = executedQty
        };
    }
}
