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
    private readonly IExchangeInstrumentRules _instrumentRules;
    private readonly ILogger<TradingExecutionService> _logger;

    public TradingExecutionService(
        IOrderValidator validator,
        IOrderBuilder builder,
        IExchangeTradingGateway gateway,
        IExchangeInstrumentRules instrumentRules,
        ILogger<TradingExecutionService> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _instrumentRules = instrumentRules ?? throw new ArgumentNullException(nameof(instrumentRules));
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

        _logger.LogInformation("OrderValidationStarted: Validating request with ID {RequestId} and OrderRequest for Symbol {Symbol}", request.Id, orderRequest.Symbol);

        // Get instrument rules from the provider
        var instrumentRules = _instrumentRules.GetInstrumentRules(orderRequest.Symbol);

        // Run the deterministic validation pipeline
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

            return ExecutionResult.CreateFailure(
                $"Validation failed: {string.Join("; ", validationResult.Errors)}",
                validationResult.ValidationCodes.Count > 0 ? validationResult.ValidationCodes[0] : "VALIDATION_FAILED",
                OrderStatus.ValidationFailed);
        }

        _logger.LogInformation("OrderValidationPassed: Request with ID {RequestId} is valid.", request.Id);

        _logger.LogInformation("ExchangeOrderSubmissionStarted: Submitting order request for Symbol {Symbol} to exchange gateway.", orderRequest.Symbol);

        OrderResult gatewayResult;
        try
        {
            gatewayResult = await _gateway.CreateOrderAsync(orderRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExchangeSubmissionException: Failed to submit order request to exchange gateway.");
            return ExecutionResult.CreateFailure($"Exchange submission failed: {ex.Message}", "EXCHANGE_SUBMISSION_ERROR", OrderStatus.Failed);
        }

        if (!gatewayResult.Success)
        {
            _logger.LogWarning("ExchangeOrderSubmissionFailed: Order submission failed. Error: {Error}, Code: {Code}", gatewayResult.ErrorMessage, gatewayResult.ErrorCode);
            return ExecutionResult.CreateFailure(gatewayResult.ErrorMessage, gatewayResult.ErrorCode ?? "EXCHANGE_ERROR", gatewayResult.Status);
        }

        _logger.LogInformation("ExchangeOrderSubmissionCompleted: Order submitted successfully. ExchangeOrderId {ExchangeOrderId}, Status {Status}",
            gatewayResult.ExchangeOrderId, gatewayResult.Status);

        return new ExecutionResult
        {
            Success = true,
            ExchangeOrderId = gatewayResult.ExchangeOrderId,
            Status = gatewayResult.Status,
            Message = "Order executed successfully on the exchange."
        };
    }
}
