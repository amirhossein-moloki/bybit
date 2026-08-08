using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Application.Services;

public class TakeProfitManager : ITakeProfitManager
{
    private readonly IPositionRepository _positionRepository;
    private readonly IExchangeTradingGateway _exchangeGateway;
    private readonly IExchangeInstrumentRules _instrumentRules;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TakeProfitManager> _logger;

    public TakeProfitManager(
        IPositionRepository positionRepository,
        IExchangeTradingGateway exchangeGateway,
        IExchangeInstrumentRules instrumentRules,
        IUnitOfWork unitOfWork,
        ILogger<TakeProfitManager> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _exchangeGateway = exchangeGateway ?? throw new ArgumentNullException(nameof(exchangeGateway));
        _instrumentRules = instrumentRules ?? throw new ArgumentNullException(nameof(instrumentRules));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<PositionTarget>> CreateTakeProfitTargetsAsync(
        Guid positionId,
        List<(decimal Price, decimal Percentage)> targetsInput,
        CancellationToken cancellationToken = default)
    {
        if (targetsInput == null || !targetsInput.Any())
        {
            throw new ArgumentException("Take profit targets cannot be null or empty.", nameof(targetsInput));
        }

        _logger.LogInformation("TakeProfitTargetsCreationStarted: PositionId={PositionId}, TargetsCount={Count}",
            positionId, targetsInput.Count);

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            throw new DomainException($"Position with ID {positionId} not found.");
        }

        if (position.Status != PositionStatus.Open && position.Status != PositionStatus.PartiallyClosed)
        {
            throw new DomainException($"Cannot create Take Profit targets on a position with status {position.Status}. Position must be open.");
        }

        // 1. Sort and Validate Ordering of Targets
        List<(decimal Price, decimal Percentage)> sortedTargets;
        if (position.Side == OrderSide.Buy) // LONG
        {
            sortedTargets = targetsInput.OrderBy(t => t.Price).ToList();
            // Validate strict order
            for (int i = 0; i < sortedTargets.Count - 1; i++)
            {
                if (sortedTargets[i].Price >= sortedTargets[i + 1].Price)
                {
                    throw new DomainException("Invalid target ordering: For LONG, target prices must be strictly ascending (TP1 < TP2 < TP3).");
                }
            }
        }
        else // SHORT
        {
            sortedTargets = targetsInput.OrderByDescending(t => t.Price).ToList();
            // Validate strict order
            for (int i = 0; i < sortedTargets.Count - 1; i++)
            {
                if (sortedTargets[i].Price <= sortedTargets[i + 1].Price)
                {
                    throw new DomainException("Invalid target ordering: For SHORT, target prices must be strictly descending (TP1 > TP2 > TP3).");
                }
            }
        }

        // 2. Validate Price side-based constraints
        foreach (var target in sortedTargets)
        {
            if (target.Price <= 0)
            {
                throw new DomainException("TakeProfit target price must be greater than zero.");
            }

            if (position.Side == OrderSide.Buy) // LONG
            {
                if (target.Price <= position.EntryPrice)
                {
                    throw new DomainException($"Invalid TakeProfit price: For LONG position, TP price ({target.Price}) must be greater than EntryPrice ({position.EntryPrice}).");
                }
            }
            else // SHORT
            {
                if (target.Price >= position.EntryPrice)
                {
                    throw new DomainException($"Invalid TakeProfit price: For SHORT position, TP price ({target.Price}) must be less than EntryPrice ({position.EntryPrice}).");
                }
            }
        }

        // 3. Validate Percentage and Quantities
        var totalPercentage = sortedTargets.Sum(t => t.Percentage);
        if (totalPercentage > 100m)
        {
            throw new DomainException($"Total take profit percentage ({totalPercentage}%) cannot exceed 100%.");
        }

        var rules = _instrumentRules.GetInstrumentRules(position.Symbol);
        var createdTargets = new List<PositionTarget>();
        var totalQuantity = 0m;

        for (int i = 0; i < sortedTargets.Count; i++)
        {
            var targetInput = sortedTargets[i];
            var targetNumber = i + 1;

            // Calculate exact quantity based on percentage of original position quantity
            var rawQuantity = position.Quantity * (targetInput.Percentage / 100m);

            // Apply step size and decimal precision rules if available
            var finalQuantity = rawQuantity;
            if (rules != null)
            {
                if (rules.QuantityPrecision > 0)
                {
                    finalQuantity = Math.Round(rawQuantity, rules.QuantityPrecision);
                }

                if (rules.QuantityStep > 0)
                {
                    var remainder = finalQuantity % rules.QuantityStep;
                    if (remainder != 0 && Math.Abs(remainder - rules.QuantityStep) > 0.00000001m)
                    {
                        throw new DomainException($"TakeProfit target quantity ({finalQuantity}) must be a multiple of the allowed Quantity Step Size ({rules.QuantityStep}).");
                    }
                }

                // Check price precision and tick size for target price
                var roundedPrice = Math.Round(targetInput.Price, rules.PricePrecision);
                if (roundedPrice != targetInput.Price)
                {
                    throw new DomainException($"TakeProfit target price ({targetInput.Price}) exceeds the allowed price precision of {rules.PricePrecision} decimal places.");
                }

                if (rules.TickSize > 0)
                {
                    var remainder = targetInput.Price % rules.TickSize;
                    if (remainder != 0 && Math.Abs(remainder - rules.TickSize) > 0.00000001m)
                    {
                        throw new DomainException($"TakeProfit target price ({targetInput.Price}) must be a multiple of the allowed Tick Size ({rules.TickSize}).");
                    }
                }
            }

            if (finalQuantity <= 0)
            {
                throw new DomainException("TakeProfit target calculated quantity must be greater than zero.");
            }

            totalQuantity += finalQuantity;

            // Ensure we do not duplicate any existing target price
            if (position.Targets.Any(t => t.Price == targetInput.Price && t.Status != "Rejected" && t.Status != "Cancelled"))
            {
                throw new DomainException($"A Take Profit target at price {targetInput.Price} already exists.");
            }

            var positionTarget = new PositionTarget(
                position.Id,
                targetNumber,
                targetInput.Price,
                finalQuantity,
                targetInput.Percentage,
                "Pending"
            );

            createdTargets.Add(positionTarget);
        }

        if (totalQuantity > position.RemainingQuantity)
        {
            throw new DomainException($"Total Take Profit quantity ({totalQuantity}) cannot exceed the position's Remaining Quantity ({position.RemainingQuantity}).");
        }

        // 4. Place limit orders on Exchange
        var oppositeSide = position.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

        foreach (var target in createdTargets)
        {
            var clientOrderId = $"BOT-TP-{Guid.NewGuid():N}";
            _logger.LogInformation("TakeProfitExchangeSubmission: Creating Limit TP order. Symbol={Symbol}, Side={Side}, Price={Price}, Qty={Qty}",
                position.Symbol, oppositeSide, target.Price, target.Quantity);

            var request = new OrderRequest
            {
                Symbol = position.Symbol,
                Side = oppositeSide,
                Type = OrderType.Limit,
                Quantity = target.Quantity,
                Price = target.Price,
                ClientOrderId = clientOrderId,
                ReduceOnly = true
            };

            var orderResult = await _exchangeGateway.CreateOrderAsync(request, cancellationToken);
            if (!orderResult.Success)
            {
                _logger.LogError("TakeProfitExchangeFailed: Exchange rejected TP Limit Order at {Price}. Error={Error}",
                    target.Price, orderResult.ErrorMessage);
                throw new DomainException($"Exchange rejected Take Profit Limit Order placement: {orderResult.ErrorMessage}");
            }

            target.SetExchangeOrderId(orderResult.ExchangeOrderId ?? clientOrderId);
            target.UpdateStatus("Active");

            // Associate target with Position
            position.Targets.Add(target);

            // Record events
            var payload = $"{{ \"PositionId\": \"{position.Id}\", \"TargetNumber\": {target.TargetNumber}, \"Price\": {target.Price}, \"Quantity\": {target.Quantity}, \"Percentage\": {target.Percentage}, \"ExchangeOrderId\": \"{target.ExchangeOrderId}\" }}";
            var posEvent = new PositionEvent(position.Id, "TakeProfitCreated", payload);
            position.Events.Add(posEvent);
        }

        _positionRepository.Update(position);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("TakeProfitTargetsCreationCompleted: Successfully created and saved {Count} TP targets.",
            createdTargets.Count);

        return createdTargets;
    }
}
