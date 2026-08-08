using System;
using TradingBot.Application.Trading.Execution.Contracts;
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

        // Structural and business boundary checks as required by domain rules:

        // Rule 1: No Risk Approval => Rejected
        if (request.RiskDecision != RiskDecisionStatus.Approved)
        {
            throw new ExecutionValidationException($"Risk approval boundary violated: Risk decision is {request.RiskDecision}. Execution only allowed for Approved.");
        }

        // Rule 4: Invalid Symbol => Rejected
        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            throw new ExecutionValidationException("Symbol is required and cannot be empty.");
        }

        if (request.Symbol.Length < 3)
        {
            throw new ExecutionValidationException("Symbol must be at least 3 characters long.");
        }

        // Rule 2: Invalid Quantity => Rejected (Quantity must be > 0)
        if (request.Quantity <= 0)
        {
            throw new ExecutionValidationException($"Invalid Quantity: {request.Quantity}. Quantity must be greater than zero.");
        }

        // Rule 3: Invalid Order Type => Rejected
        if (!Enum.IsDefined(typeof(OrderType), request.OrderType))
        {
            throw new ExecutionValidationException($"Invalid Order Type: {request.OrderType}.");
        }

        if (!Enum.IsDefined(typeof(OrderSide), request.Side))
        {
            throw new ExecutionValidationException($"Invalid Order Side: {request.Side}.");
        }

        // Rule 6/Validation: Valid Limit Order requires Price > 0
        if (request.OrderType == OrderType.Limit && request.Price <= 0)
        {
            throw new ExecutionValidationException("Limit Price must be greater than zero when OrderType is Limit.");
        }
    }
}
