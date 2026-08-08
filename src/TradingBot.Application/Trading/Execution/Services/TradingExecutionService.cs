using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Exceptions;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Enums;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.Application.Trading.Execution.Services;

public class TradingExecutionService : ITradeExecutionService
{
    private readonly IOrderValidator _validator;
    private readonly IOrderBuilder _builder;
    private readonly IExchangeTradingGateway _gateway;
    private readonly IExchangeInstrumentRules _instrumentRules;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderEventRepository _orderEventRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TradingExecutionService> _logger;

    public TradingExecutionService(
        IOrderValidator validator,
        IOrderBuilder builder,
        IExchangeTradingGateway gateway,
        IExchangeInstrumentRules instrumentRules,
        IOrderRepository orderRepository,
        IOrderEventRepository orderEventRepository,
        IUnitOfWork unitOfWork,
        ILogger<TradingExecutionService> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _instrumentRules = instrumentRules ?? throw new ArgumentNullException(nameof(instrumentRules));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Obsolete("Use the primary constructor with repositories and unit of work.", false)]
    public TradingExecutionService(
        IOrderValidator validator,
        IOrderBuilder builder,
        IExchangeTradingGateway gateway,
        IExchangeInstrumentRules instrumentRules,
        ILogger<TradingExecutionService> logger)
        : this(
            validator,
            builder,
            gateway,
            instrumentRules,
            new NoOpOrderRepository(),
            new NoOpOrderEventRepository(),
            new NoOpUnitOfWork(),
            logger)
    {
    }

    public async Task<ExecutionResult> ExecuteAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation("ExecutionStarted: Received execution request with ID {RequestId} for SignalId {SignalId}, Symbol {Symbol}, Side {Side}, Type {Type}, Quantity {Quantity}",
            request.Id, request.SignalId, request.Symbol, request.Side, request.OrderType, request.Quantity);

        // 1. Idempotency Check (Database & Application Protection)
        var existingOrder = await _orderRepository.GetBySignalIdAsync(request.SignalId, cancellationToken);
        if (existingOrder != null)
        {
            _logger.LogInformation("ExecutionDuplicateFound: Found existing local order {OrderId} in status {Status} for SignalId {SignalId}. Returning existing execution.",
                existingOrder.Id, existingOrder.Status, request.SignalId);

            bool success = existingOrder.Status == OrderStatus.Filled ||
                           existingOrder.Status == OrderStatus.PartiallyFilled ||
                           existingOrder.Status == OrderStatus.Accepted ||
                           existingOrder.Status == OrderStatus.New ||
                           existingOrder.Status == OrderStatus.Submitted;

            return new ExecutionResult
            {
                Success = success,
                OrderId = existingOrder.Id,
                ExchangeOrderId = existingOrder.ExchangeOrderId,
                Status = existingOrder.Status,
                Message = $"Duplicate request detected. Found existing order in status {existingOrder.Status}."
            };
        }

        // 2. Build Order Parameters
        _logger.LogInformation("OrderBuildStarted: Constructing OrderRequest from TradeExecutionRequest for SignalId {SignalId}", request.SignalId);
        OrderRequest orderRequest;
        try
        {
            orderRequest = _builder.Build(request);
            _logger.LogInformation("OrderBuildCompleted: OrderRequest constructed with temporary ClientOrderId {ClientOrderId}", orderRequest.ClientOrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderConstructionException: Failed to construct OrderRequest for request ID {RequestId}", request.Id);
            throw new OrderConstructionException("Failed to construct order request from trade execution request.", ex);
        }

        // 3. Run Deterministic Validation Pipeline
        _logger.LogInformation("OrderValidationStarted: Validating OrderRequest for Symbol {Symbol}", orderRequest.Symbol);
        var instrumentRules = _instrumentRules.GetInstrumentRules(orderRequest.Symbol);

        OrderValidationResult validationResult;
        try
        {
            validationResult = _validator.Validate(request, orderRequest, instrumentRules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during validation of request with ID {RequestId}", request.Id);
            return ExecutionResult.CreateFailure($"Unexpected validation error: {ex.Message}", "UNKNOWN_VALIDATION_ERROR", OrderStatus.Failed);
        }

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("OrderValidationFailed: Request with ID {RequestId} failed validation. Codes: {Codes}", request.Id, string.Join(", ", validationResult.ValidationCodes));
            _logger.LogWarning("OrderRejected: Validation failed. Message: {Message}", string.Join("; ", validationResult.Errors));

            // Persist the failed validation order for complete traceback audit trail
            var orderId = Guid.NewGuid();
            var failedOrder = new Order(
                orderId,
                $"TB-{orderId:N}",
                new Symbol(orderRequest.Symbol),
                orderRequest.Side,
                orderRequest.Type,
                new Quantity(orderRequest.Quantity),
                new Money(orderRequest.Price),
                request.SignalId);

            try
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                failedOrder.UpdateStatus(OrderStatus.ValidationFailed);
                failedOrder.SetFailure(string.Join("; ", validationResult.Errors), "VALIDATION_FAILED");
                await _orderRepository.AddAsync(failedOrder, cancellationToken);

                var failedEvent = new OrderEvent(
                    failedOrder.Id,
                    OrderStatus.Created,
                    OrderStatus.ValidationFailed,
                    "OrderRejected",
                    "TradingExecutionService",
                    $"Validation failed: {string.Join("; ", validationResult.Errors)}");
                await _orderEventRepository.AddAsync(failedEvent, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist validation failed order in database.");
                try { await _unitOfWork.RollbackTransactionAsync(cancellationToken); } catch { }
            }

            return ExecutionResult.CreateFailure(
                $"Validation failed: {string.Join("; ", validationResult.Errors)}",
                validationResult.ValidationCodes.Count > 0 ? validationResult.ValidationCodes[0] : "VALIDATION_FAILED",
                OrderStatus.ValidationFailed);
        }

        _logger.LogInformation("OrderValidationPassed: Request with ID {RequestId} is valid.", request.Id);

        // 4. Pre-generate and Persist Local Order as Pending (Transaction Boundary 1)
        var preGeneratedOrderId = Guid.NewGuid();
        var clientOrderId = $"TB-{preGeneratedOrderId:N}";
        orderRequest.ClientOrderId = clientOrderId; // set the deterministic client order ID on request

        var order = new Order(
            preGeneratedOrderId,
            clientOrderId,
            new Symbol(orderRequest.Symbol),
            orderRequest.Side,
            orderRequest.Type,
            new Quantity(orderRequest.Quantity),
            new Money(orderRequest.Price),
            request.SignalId);

        order.UpdateStatus(OrderStatus.Pending);

        _logger.LogInformation("LocalOrderCreated: Persisting local order {OrderId} as Pending BEFORE submitting to exchange.", order.Id);
        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await _orderRepository.AddAsync(order, cancellationToken);

            var createdEvent = new OrderEvent(
                order.Id,
                OrderStatus.Created,
                OrderStatus.Pending,
                "OrderCreated",
                "TradingExecutionService",
                "Local order created successfully in database.");
            await _orderEventRepository.AddAsync(createdEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseException: Failed to persist local Pending order. Aborting submission to prevent double execution risk.");
            try { await _unitOfWork.RollbackTransactionAsync(cancellationToken); } catch { }
            throw;
        }

        // Transition local order state to Submitting and persist state change BEFORE external network request
        _logger.LogInformation("ExchangeSubmissionStarted: Transitioning local order {OrderId} to Submitting and sending request to Exchange.", order.Id);
        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            order.UpdateStatus(OrderStatus.Submitting);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            var submittingEvent = new OrderEvent(
                order.Id,
                OrderStatus.Pending,
                OrderStatus.Submitting,
                "OrderSubmissionStarted",
                "TradingExecutionService",
                "Sending submission request to exchange.");
            await _orderEventRepository.AddAsync(submittingEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transition order {OrderId} to Submitting state.", order.Id);
            try { await _unitOfWork.RollbackTransactionAsync(cancellationToken); } catch { }
        }

        // 5. Submit to Exchange (HTTP Request OUTSIDE database transaction)
        OrderResult gatewayResult;
        bool isTimeout = false;

        try
        {
            gatewayResult = await _gateway.CreateOrderAsync(orderRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException || ex.InnerException is TimeoutException or TaskCanceledException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "ExchangeSubmissionUnknown: Timeout occurred during CreateOrderAsync for client ID {ClientOrderId}. Marking as Unknown for reconciliation.", clientOrderId);
            gatewayResult = new OrderResult
            {
                Success = false,
                Status = OrderStatus.Unknown,
                ErrorMessage = "Timeout/Network exception occurred during submission.",
                ErrorCode = "TIMEOUT",
                ErrorType = ExchangeErrorType.Unavailable
            };
            isTimeout = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExchangeSubmissionException: Unexpected exception during CreateOrderAsync. Mapping to Unknown to avoid duplicate creations.");
            gatewayResult = new OrderResult
            {
                Success = false,
                Status = OrderStatus.Unknown,
                ErrorMessage = $"Network or connection error: {ex.Message}",
                ErrorCode = "CONNECTION_ERROR",
                ErrorType = ExchangeErrorType.Unknown
            };
            isTimeout = true;
        }

        // 6. Update Local Order State after Submission (Transaction Boundary 2)
        _logger.LogInformation("ExchangeSubmissionCompleted: Processing exchange response. Success={Success}, Status={Status}", gatewayResult.Success, gatewayResult.Status);

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var previousStatus = order.Status;

            if (gatewayResult.Success)
            {
                order.SetExchangeDetails(gatewayResult.ExchangeOrderId ?? throw new Exception("Successful submission must return ExchangeOrderId."), "Bybit");
                order.UpdateStatus(gatewayResult.Status);

                var successEvent = new OrderEvent(
                    order.Id,
                    previousStatus,
                    order.Status,
                    "ExchangeSubmissionSucceeded",
                    "TradingExecutionService",
                    $"Order submitted successfully. ExchangeOrderId: {order.ExchangeOrderId}");
                await _orderEventRepository.AddAsync(successEvent, cancellationToken);

                await _orderRepository.UpdateAsync(order, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                return new ExecutionResult
                {
                    Success = true,
                    OrderId = order.Id,
                    ExchangeOrderId = order.ExchangeOrderId,
                    Status = order.Status,
                    Message = "Order executed successfully on the exchange."
                };
            }
            else
            {
                // Classify Error (Section 18 & Section 19)
                bool isTemporary = isTimeout ||
                                   gatewayResult.ErrorType == ExchangeErrorType.Unavailable ||
                                   gatewayResult.ErrorType == ExchangeErrorType.RateLimited ||
                                   gatewayResult.ErrorCode == "TIMEOUT" ||
                                   gatewayResult.ErrorCode == "NULL_RESPONSE" ||
                                   gatewayResult.ErrorCode == "EXCEPTION" ||
                                   gatewayResult.ErrorCode == "CONNECTION_ERROR";

                if (isTemporary)
                {
                    // Action: Unknown -> Reconciliation. Never retry blind CreateOrder
                    order.UpdateStatus(OrderStatus.Unknown);
                    order.SetFailure(gatewayResult.ErrorMessage, gatewayResult.ErrorCode);

                    var tempFailureEvent = new OrderEvent(
                        order.Id,
                        previousStatus,
                        OrderStatus.Unknown,
                        "OrderSubmissionUnknown",
                        "TradingExecutionService",
                        $"Temporary error: {gatewayResult.ErrorMessage} ({gatewayResult.ErrorCode}). Marked as Unknown for background reconciliation.");
                    await _orderEventRepository.AddAsync(tempFailureEvent, cancellationToken);

                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);

                    return ExecutionResult.CreateFailure(
                        $"Temporary exchange error occurred. Order marked as Unknown for reconciliation: {gatewayResult.ErrorMessage}",
                        gatewayResult.ErrorCode ?? "TEMP_EXCHANGE_ERROR",
                        OrderStatus.Unknown);
                }
                else
                {
                    // Action: Permanent error -> Failed / Rejected. No retry.
                    var finalStatus = gatewayResult.Status == OrderStatus.Rejected ? OrderStatus.Rejected : OrderStatus.Failed;
                    order.UpdateStatus(finalStatus);
                    order.SetFailure(gatewayResult.ErrorMessage, gatewayResult.ErrorCode);

                    var permFailureEvent = new OrderEvent(
                        order.Id,
                        previousStatus,
                        finalStatus,
                        gatewayResult.Status == OrderStatus.Rejected ? "OrderRejected" : "OrderFailed",
                        "TradingExecutionService",
                        $"Permanent error: {gatewayResult.ErrorMessage} ({gatewayResult.ErrorCode}). Order rejected/failed.");
                    await _orderEventRepository.AddAsync(permFailureEvent, cancellationToken);

                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);

                    return ExecutionResult.CreateFailure(
                        gatewayResult.ErrorMessage,
                        gatewayResult.ErrorCode ?? "EXCHANGE_ERROR",
                        finalStatus);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatabaseException: Failed to persist the final state for order {OrderId}. State is inconsistent. Manual intervention may be required.", order.Id);
            try { await _unitOfWork.RollbackTransactionAsync(cancellationToken); } catch { }
            throw;
        }
    }

    private class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private class NoOpOrderRepository : IOrderRepository
    {
        public Task AddAsync(Order entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<Order>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
        public Task<IEnumerable<Order>> GetAsync(ISpecification<Order> spec, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<PagedResult<Order>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult<PagedResult<Order>>(null!);
        public Task<PagedResult<Order>> GetPagedAsync(ISpecification<Order> spec, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult<PagedResult<Order>>(null!);
        public void Remove(Order entity) { }
        public void Update(Order entity) { }
        public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<Order>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
        public Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<Order?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
        public Task<IEnumerable<Order>> GetOrdersBySymbolAsync(string symbol, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
        public Task<IEnumerable<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
        public Task<PagedResult<Order>> GetPagedOrdersAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult<PagedResult<Order>>(null!);
        public Task<Order?> GetBySignalIdAsync(Guid signalId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IEnumerable<Order>> GetPendingReconciliationOrdersAsync(int batchSize, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Order>>(Array.Empty<Order>());
    }

    private class NoOpOrderEventRepository : IOrderEventRepository
    {
        public Task AddAsync(OrderEvent entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<OrderEvent>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OrderEvent>>(Array.Empty<OrderEvent>());
        public Task<IEnumerable<OrderEvent>> GetAsync(ISpecification<OrderEvent> spec, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OrderEvent>>(Array.Empty<OrderEvent>());
        public Task<OrderEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<OrderEvent?>(null);
        public Task<PagedResult<OrderEvent>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult<PagedResult<OrderEvent>>(null!);
        public Task<PagedResult<OrderEvent>> GetPagedAsync(ISpecification<OrderEvent> spec, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult<PagedResult<OrderEvent>>(null!);
        public void Remove(OrderEvent entity) { }
        public void Update(OrderEvent entity) { }
        public Task<IEnumerable<OrderEvent>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<OrderEvent>>(Array.Empty<OrderEvent>());
    }
}
