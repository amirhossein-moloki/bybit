using System;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Enums;
using TradingBot.Application.Trading.Execution.Exceptions;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class OrderValidator : IOrderValidator
{
    public void Validate(TradeExecutionRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Construct temporary OrderRequest for validation
        var orderRequest = new OrderRequest
        {
            Symbol = SymbolNormalizer.Normalize(request.Symbol),
            Side = request.Side,
            Type = request.OrderType,
            Quantity = request.Quantity,
            Price = request.Price,
            SignalId = request.SignalId,
            RiskEvaluationId = request.RiskEvaluationId,
            ClientOrderId = "TEMP-COMPATIBILITY-ID"
        };

        // Perform validation. If any error, throw ExecutionValidationException.
        var result = Validate(request, orderRequest, null);

        foreach (var error in result.Errors)
        {
            int index = result.Errors.IndexOf(error);
            var code = result.ValidationCodes[index];
            if (code == "MISSING_INSTRUMENT_RULES")
            {
                continue;
            }
            throw new ExecutionValidationException(error);
        }
    }

    public OrderValidationResult Validate(TradeExecutionRequest executionRequest, OrderRequest orderRequest, InstrumentRules? instrumentRules)
    {
        if (executionRequest == null) throw new ArgumentNullException(nameof(executionRequest));
        if (orderRequest == null) throw new ArgumentNullException(nameof(orderRequest));

        var result = new OrderValidationResult();

        // 1. Risk Approval Validation
        if (executionRequest.RiskDecision != RiskDecisionStatus.Approved)
        {
            result.AddError($"Risk approval boundary violated: Risk decision is {executionRequest.RiskDecision}. Execution only allowed for Approved.", "RISK_APPROVAL_REQUIRED", ValidationSeverity.Critical);
        }

        // 2. Symbol Validation
        if (string.IsNullOrWhiteSpace(orderRequest.Symbol))
        {
            result.AddError("Symbol is required and cannot be empty.", "INVALID_SYMBOL", ValidationSeverity.Critical);
        }
        else if (orderRequest.Symbol.Length < 3)
        {
            result.AddError("Symbol must be at least 3 characters long.", "INVALID_SYMBOL", ValidationSeverity.Critical);
        }

        // 3. Side Validation
        if (!Enum.IsDefined(typeof(OrderSide), orderRequest.Side))
        {
            result.AddError($"Invalid Order Side: {orderRequest.Side}.", "INVALID_SIDE", ValidationSeverity.Critical);
        }

        // 4. Order Type Validation
        if (!Enum.IsDefined(typeof(OrderType), orderRequest.Type))
        {
            result.AddError($"Invalid Order Type: {orderRequest.Type}.", "INVALID_ORDER_TYPE", ValidationSeverity.Critical);
        }

        // 5. Quantity Validation (Basic check)
        if (orderRequest.Quantity <= 0)
        {
            result.AddError($"Invalid Quantity: {orderRequest.Quantity}. Quantity must be greater than zero.", "INVALID_QUANTITY", ValidationSeverity.Critical);
        }

        // 6. Price Validation
        if (orderRequest.Type == OrderType.Limit)
        {
            if (orderRequest.Price <= 0)
            {
                result.AddError("Limit Price must be greater than zero when OrderType is Limit.", "INVALID_LIMIT_PRICE", ValidationSeverity.Critical);
            }
        }

        // 7. Instrument Constraint Validation (Fail-Closed Behavior)
        if (instrumentRules == null)
        {
            result.AddError($"Missing instrument rules for Symbol {orderRequest.Symbol ?? "UNKNOWN"}. Fail-closed.", "MISSING_INSTRUMENT_RULES", ValidationSeverity.Critical);
            return result;
        }

        // 8. Specific Instrument Rule Validations
        if (orderRequest.Quantity > 0)
        {
            // Minimum Quantity
            if (orderRequest.Quantity < instrumentRules.MinQuantity)
            {
                result.AddError($"Requested quantity {orderRequest.Quantity} is below minimum allowed {instrumentRules.MinQuantity}.", "QUANTITY_BELOW_MINIMUM", ValidationSeverity.Error);
            }

            // Maximum Quantity
            if (instrumentRules.MaxQuantity.HasValue && orderRequest.Quantity > instrumentRules.MaxQuantity.Value)
            {
                result.AddError($"Requested quantity {orderRequest.Quantity} is above maximum allowed {instrumentRules.MaxQuantity.Value}.", "QUANTITY_ABOVE_MAXIMUM", ValidationSeverity.Error);
            }

            // Quantity Step
            decimal quantityRemainder = orderRequest.Quantity % instrumentRules.QuantityStep;
            decimal tolerance = 1e-10m;
            if (quantityRemainder > tolerance && (instrumentRules.QuantityStep - quantityRemainder) > tolerance)
            {
                result.AddError($"Requested quantity {orderRequest.Quantity} does not satisfy the exchange's QuantityStep of {instrumentRules.QuantityStep}.", "INVALID_QUANTITY_STEP", ValidationSeverity.Error);
            }
        }

        // Price Constraint validations
        if (orderRequest.Type == OrderType.Limit && orderRequest.Price > 0)
        {
            // Tick Size
            decimal priceRemainder = orderRequest.Price % instrumentRules.TickSize;
            decimal tolerance = 1e-10m;
            if (priceRemainder > tolerance && (instrumentRules.TickSize - priceRemainder) > tolerance)
            {
                result.AddError($"Requested price {orderRequest.Price} does not satisfy the exchange's TickSize of {instrumentRules.TickSize}.", "INVALID_PRICE_TICK", ValidationSeverity.Error);
            }

            // Price below minimum tick check
            if (orderRequest.Price < instrumentRules.TickSize)
            {
                result.AddError($"Requested price {orderRequest.Price} is below the minimum tick size of {instrumentRules.TickSize}.", "PRICE_BELOW_MINIMUM", ValidationSeverity.Error);
            }
        }

        // Notional Validation
        decimal notional = 0;
        if (orderRequest.Type == OrderType.Limit)
        {
            notional = orderRequest.Quantity * orderRequest.Price;
        }
        else if (orderRequest.Type == OrderType.Market && executionRequest.Price > 0)
        {
            notional = orderRequest.Quantity * executionRequest.Price;
        }

        if (notional > 0 && notional < instrumentRules.MinNotional)
        {
            result.AddError($"Requested order notional {notional} is below minimum allowed notional of {instrumentRules.MinNotional}.", "NOTIONAL_BELOW_MINIMUM", ValidationSeverity.Error);
        }

        return result;
    }
}
