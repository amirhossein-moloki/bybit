using System;
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

public class PartialCloseManager : IPartialCloseManager
{
    private readonly IPositionRepository _positionRepository;
    private readonly IExchangeTradingGateway _exchangeGateway;
    private readonly IExchangeInstrumentRules _instrumentRules;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PartialCloseManager> _logger;

    public PartialCloseManager(
        IPositionRepository positionRepository,
        IExchangeTradingGateway exchangeGateway,
        IExchangeInstrumentRules instrumentRules,
        IUnitOfWork unitOfWork,
        ILogger<PartialCloseManager> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _exchangeGateway = exchangeGateway ?? throw new ArgumentNullException(nameof(exchangeGateway));
        _instrumentRules = instrumentRules ?? throw new ArgumentNullException(nameof(instrumentRules));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecutePartialCloseAsync(
        Guid positionId,
        decimal quantity,
        decimal? price = null,
        string reason = "Partial Close",
        string source = "System",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PartialCloseStarted: PositionId={PositionId}, Qty={Quantity}, Price={Price}, Reason={Reason}",
            positionId, quantity, price, reason);

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            throw new DomainException($"Position with ID {positionId} not found.");
        }

        if (position.Status != PositionStatus.Open && position.Status != PositionStatus.PartiallyClosed)
        {
            throw new DomainException($"Invalid transition: Cannot partially close a position in {position.Status} state.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Close quantity must be greater than zero.");
        }

        if (quantity > position.RemainingQuantity)
        {
            throw new DomainException("Cannot close more than the remaining position quantity.");
        }

        // 1. Validate quantity step size
        var rules = _instrumentRules.GetInstrumentRules(position.Symbol);
        if (rules != null && rules.QuantityStep > 0)
        {
            var remainder = quantity % rules.QuantityStep;
            if (remainder != 0 && Math.Abs(remainder - rules.QuantityStep) > 0.00000001m)
            {
                throw new DomainException($"Quantity ({quantity}) must be a multiple of the allowed Step Size ({rules.QuantityStep}).");
            }
        }

        // 2. Submit close order to Exchange
        var oppositeSide = position.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        var clientOrderId = $"BOT-CLOSE-{Guid.NewGuid():N}";

        var request = new OrderRequest
        {
            Symbol = position.Symbol,
            Side = oppositeSide,
            Type = price.HasValue ? OrderType.Limit : OrderType.Market,
            Quantity = quantity,
            Price = price ?? 0m,
            ClientOrderId = clientOrderId,
            ReduceOnly = true
        };

        _logger.LogInformation("PartialCloseExchangeSubmission: Submitting close order to exchange for Symbol={Symbol}, Qty={Qty}, Type={Type}",
            position.Symbol, quantity, request.Type);

        var orderResult = await _exchangeGateway.CreateOrderAsync(request, cancellationToken);
        if (!orderResult.Success)
        {
            _logger.LogError("PartialCloseExchangeFailed: Exchange rejected close order. Error={Error}",
                orderResult.ErrorMessage);
            return false;
        }

        // 3. Update position after confirmed execution
        var execQty = orderResult.ExecutedQuantity > 0 ? orderResult.ExecutedQuantity : quantity;
        var execPrice = orderResult.ExecutedPrice > 0 ? orderResult.ExecutedPrice : (price ?? position.CurrentPrice);

        var previousStatus = position.Status;
        position.PartialClose(execQty, execPrice);

        // Record events
        var eventType = position.Status == PositionStatus.Closed ? "PositionClosed" : "PositionPartiallyClosed";
        var payload = $"{{ \"PositionId\": \"{position.Id}\", \"PreviousStatus\": \"{previousStatus}\", \"NewStatus\": \"{position.Status}\", \"ClosedQuantity\": {execQty}, \"ExecutionPrice\": {execPrice}, \"ExchangeOrderId\": \"{orderResult.ExchangeOrderId}\" }}";
        var posEvent = new PositionEvent(position.Id, eventType, payload);
        position.Events.Add(posEvent);

        _positionRepository.Update(position);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PartialCloseCompleted: Successfully closed {Qty} at {Price} on exchange and DB for PositionId={PositionId}. New RemainingQuantity={Remaining}",
            execQty, execPrice, position.Id, position.RemainingQuantity);

        return true;
    }

    public async Task<bool> ProcessTakeProfitHitAsync(
        string exchangeOrderId,
        decimal executedQuantity,
        decimal executedPrice,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            throw new ArgumentException("Exchange Order ID cannot be null or empty.", nameof(exchangeOrderId));
        }

        _logger.LogInformation("ProcessTakeProfitHitStarted: ExchangeOrderId={ExchangeOrderId}, ExecQty={Qty}, ExecPrice={Price}",
            exchangeOrderId, executedQuantity, executedPrice);

        // Find the open position that owns a target with this exchangeOrderId
        var openPositions = await _positionRepository.GetOpenPositionsAsync(cancellationToken);
        Position? position = null;
        PositionTarget? target = null;

        foreach (var p in openPositions)
        {
            var fullPosition = await _positionRepository.GetByIdAsync(p.Id, cancellationToken);
            if (fullPosition != null)
            {
                var t = fullPosition.Targets.FirstOrDefault(x => x.ExchangeOrderId == exchangeOrderId);
                if (t != null)
                {
                    position = fullPosition;
                    target = t;
                    break;
                }
            }
        }

        if (position == null || target == null)
        {
            _logger.LogWarning("ProcessTakeProfitHitFailed: No open PositionTarget found matching ExchangeOrderId={ExchangeOrderId}.",
                exchangeOrderId);
            return false;
        }

        // 1. Idempotency Check (Duplicate TP Protection)
        if (target.Status == "Executed")
        {
            _logger.LogInformation("ProcessTakeProfitHitIdempotent: Target {TargetId} with ExchangeOrderId={ExchangeOrderId} is already executed. Skipping to prevent duplicate closes.",
                target.Id, exchangeOrderId);
            return true;
        }

        _logger.LogInformation("ProcessTakeProfitHitExecuting: Identified Position {PositionId} and Target {TargetId} (TP#{Num}).",
            position.Id, target.Id, target.TargetNumber);

        // 2. Update the Target state
        target.MarkExecuted(executedQuantity);

        // 3. Execute partial close on the Position
        var previousStatus = position.Status;
        position.PartialClose(executedQuantity, executedPrice);

        // 4. Create and record event
        var payload = $"{{ \"PositionId\": \"{position.Id}\", \"TargetId\": \"{target.Id}\", \"TargetNumber\": {target.TargetNumber}, \"ExecutedQuantity\": {executedQuantity}, \"ExecutedPrice\": {executedPrice}, \"ExchangeOrderId\": \"{exchangeOrderId}\" }}";
        var posEvent = new PositionEvent(position.Id, "TakeProfitHit", payload);
        position.Events.Add(posEvent);

        var closeEventType = position.Status == PositionStatus.Closed ? "PositionClosed" : "PositionPartiallyClosed";
        var closePayload = $"{{ \"PositionId\": \"{position.Id}\", \"PreviousStatus\": \"{previousStatus}\", \"NewStatus\": \"{position.Status}\", \"ClosedQuantity\": {executedQuantity}, \"ExecutionPrice\": {executedPrice}, \"ExchangeOrderId\": \"{exchangeOrderId}\" }}";
        var closeEvent = new PositionEvent(position.Id, closeEventType, closePayload);
        position.Events.Add(closeEvent);

        _positionRepository.Update(position);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("ProcessTakeProfitHitCompleted: Successfully processed TP target execution for PositionId={PositionId}.",
            position.Id);

        return true;
    }
}
