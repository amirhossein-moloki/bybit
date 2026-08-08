using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Exceptions;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class TradingExecutionService : ITradeExecutionService
{
    private readonly IOrderValidator _validator;
    private readonly IOrderBuilder _builder;
    private readonly IExchangeTradingGateway _gateway;
    private readonly ILogger<TradingExecutionService> _logger;

    public TradingExecutionService(
        IOrderValidator validator,
        IOrderBuilder builder,
        IExchangeTradingGateway gateway,
        ILogger<TradingExecutionService> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutionResult> ExecuteAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation("ExecutionRequested: Received request for SignalId {SignalId}, Symbol {Symbol}, Side {Side}, Type {Type}, Quantity {Quantity}",
            request.SignalId, request.Symbol, request.Side, request.OrderType, request.Quantity);

        _logger.LogInformation("ExecutionValidationStarted: Validating request with ID {RequestId}", request.Id);

        try
        {
            _validator.Validate(request);
            _logger.LogInformation("ExecutionValidationPassed: Request with ID {RequestId} is valid.", request.Id);
        }
        catch (ExecutionValidationException ex)
        {
            _logger.LogWarning("ExecutionValidationFailed: Request with ID {RequestId} failed validation. Reason: {Reason}", request.Id, ex.Message);
            return ExecutionResult.CreateFailure($"Validation failed: {ex.Message}", "VALIDATION_FAILED", OrderStatus.Rejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during validation of request with ID {RequestId}", request.Id);
            return ExecutionResult.CreateFailure($"Unexpected validation error: {ex.Message}", "UNKNOWN_VALIDATION_ERROR", OrderStatus.Failed);
        }

        _logger.LogInformation("OrderBuildStarted: Constructing OrderRequest from TradeExecutionRequest for SignalId {SignalId}", request.SignalId);
        OrderRequest orderRequest;
        try
        {
            orderRequest = _builder.Build(request);
            _logger.LogInformation("OrderBuildCompleted: OrderRequest constructed successfully with ClientOrderId {ClientOrderId}", orderRequest.ClientOrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderConstructionException: Failed to construct OrderRequest for request ID {RequestId}", request.Id);
            throw new OrderConstructionException("Failed to construct order request from trade execution request.", ex);
        }

        _logger.LogInformation("Sending order to Exchange Gateway for ClientOrderId {ClientOrderId}", orderRequest.ClientOrderId);
        try
        {
            var gatewayResult = await _gateway.CreateOrderAsync(orderRequest, cancellationToken);

            if (gatewayResult.Success)
            {
                _logger.LogInformation("ExecutionCompleted: Order execution succeeded. ExchangeOrderId: {ExchangeOrderId}", gatewayResult.ExchangeOrderId);
                return ExecutionResult.CreateSuccess(Guid.NewGuid(), gatewayResult.ExchangeOrderId ?? string.Empty, "Order executed successfully via Gateway.");
            }
            else
            {
                _logger.LogWarning("ExecutionFailed: Exchange gateway returned failure. Code: {Code}, Error: {Error}", gatewayResult.ErrorCode, gatewayResult.ErrorMessage);
                return ExecutionResult.CreateFailure(gatewayResult.ErrorMessage, gatewayResult.ErrorCode, gatewayResult.Status);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ExecutionCancelled: Order execution cancelled for ClientOrderId {ClientOrderId}", orderRequest.ClientOrderId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExchangeGatewayException: Unexpected gateway error while placing order for ClientOrderId {ClientOrderId}", orderRequest.ClientOrderId);
            throw new ExchangeGatewayException("Unexpected exchange gateway failure.", ex);
        }
    }
}
