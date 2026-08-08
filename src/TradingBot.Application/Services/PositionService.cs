using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Application.Services;

public class PositionService : IPositionService
{
    private readonly IPositionRepository _positionRepository;
    private readonly ISignalRepository _signalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PositionService> _logger;

    public PositionService(
        IPositionRepository positionRepository,
        ISignalRepository signalRepository,
        IUnitOfWork unitOfWork,
        ILogger<PositionService> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Position> CreatePositionFromOrderAsync(
        Order order,
        IEnumerable<PositionTarget>? targets = null,
        CancellationToken cancellationToken = default)
    {
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        _logger.LogInformation("PositionCreationStarted: Beginning position creation workflow for OrderId: {OrderId}", order.Id);

        // 1. Validate required execution identity
        if (order.Id == Guid.Empty)
        {
            _logger.LogWarning("PositionCreationRejected: Order ID is empty.");
            throw new DomainException("Required execution identity (OrderId) is missing.");
        }

        // 2. Validate Order execution status
        if (order.Status != OrderStatus.Filled && order.Status != OrderStatus.PartiallyFilled)
        {
            _logger.LogWarning("PositionCreationRejected: Order execution status is invalid ({Status}).", order.Status);
            throw new DomainException($"Cannot create a position from an order with status {order.Status}. Order must be Filled or PartiallyFilled.");
        }

        // 3. Validate Symbol
        if (order.Symbol == null || string.IsNullOrWhiteSpace(order.Symbol.Value))
        {
            _logger.LogWarning("PositionCreationRejected: Order symbol is invalid.");
            throw new DomainException("Symbol is invalid.");
        }

        // 4. Validate Quantity
        decimal qty = order.ExecutedQuantity > 0 ? order.ExecutedQuantity : order.Quantity.Value;
        if (qty <= 0)
        {
            _logger.LogWarning("PositionCreationRejected: Executed quantity is invalid ({Quantity}).", qty);
            throw new DomainException("Quantity is invalid.");
        }

        // 5. Validate Entry Price
        decimal price = order.ExecutedPrice > 0 ? order.ExecutedPrice : order.Price.Amount;
        if (price <= 0)
        {
            _logger.LogWarning("PositionCreationRejected: Executed price is invalid ({Price}).", price);
            throw new DomainException("Entry price is invalid.");
        }

        // 6. Idempotency Check
        var existing = await _positionRepository.GetByOrderIdAsync(order.Id, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("PositionCreationDuplicate: Position already exists for OrderId: {OrderId}. Duplicate creation prevented.", order.Id);
            return existing;
        }

        // 7. Load Signal details if linked to enrich position (StopLoss, TakeProfit, Leverage)
        Signal? signal = null;
        if (order.SignalId.HasValue && order.SignalId.Value != Guid.Empty)
        {
            signal = await _signalRepository.GetByIdAsync(order.SignalId.Value, cancellationToken);
        }

        decimal? stopLoss = signal?.StopLoss;
        decimal? takeProfit = signal?.TakeProfit;
        decimal? leverage = signal?.Leverage;

        // Create the Position domain entity
        var position = new Position(
            order.Id,
            order.Symbol.Value,
            order.Side,
            price,
            qty,
            stopLoss: stopLoss,
            takeProfit: takeProfit,
            exchangePositionId: order.ExchangeOrderId,
            leverage: leverage,
            margin: null,
            fee: 0m,
            initialStatus: PositionStatus.Open
        );

        // 8. Associate and validate Targets if provided
        if (targets != null)
        {
            foreach (var target in targets)
            {
                if (target.Price <= 0 || target.Quantity <= 0)
                {
                    _logger.LogWarning("PositionCreationRejected: Target contains invalid values.");
                    throw new DomainException("Target price and quantity must be greater than zero.");
                }

                target.SetPositionId(position.Id);
                position.Targets.Add(target);
            }
        }

        // 9. Consistently persist PositionOpened Event
        var payload = $"{{ \"OrderId\": \"{order.Id}\", \"Symbol\": \"{order.Symbol.Value}\", \"EntryPrice\": {position.EntryPrice}, \"Quantity\": {position.Quantity} }}";
        var openedEvent = new PositionEvent(position.Id, "PositionOpened", payload);
        position.Events.Add(openedEvent);

        _logger.LogInformation("PositionEventCreated: Created event {EventType} for PositionId: {PositionId}", openedEvent.EventType, position.Id);

        // Save position
        await _positionRepository.AddAsync(position, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PositionCreated: Successfully created and saved PositionId: {PositionId} for OrderId: {OrderId}", position.Id, order.Id);

        return position;
    }

    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _positionRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Position?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _positionRepository.GetByOrderIdAsync(orderId, cancellationToken);
    }

    public async Task<IEnumerable<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        return await _positionRepository.GetOpenPositionsAsync(cancellationToken);
    }

    public async Task UpdatePositionStatusAsync(Guid id, PositionStatus newStatus, string reason = "", CancellationToken cancellationToken = default)
    {
        var position = await _positionRepository.GetByIdAsync(id, cancellationToken);
        if (position == null)
        {
            throw new DomainException($"Position with ID {id} not found.");
        }

        var oldStatus = position.Status;
        if (oldStatus == newStatus) return;

        position.TransitionTo(newStatus);

        // Persist the status changed event
        var eventType = newStatus switch
        {
            PositionStatus.Closed => "PositionClosed",
            PositionStatus.PartiallyClosed => "PositionPartiallyClosed",
            PositionStatus.Liquidated => "PositionLiquidated",
            _ => "PositionStateChanged"
        };

        var payload = $"{{ \"OldStatus\": \"{oldStatus}\", \"NewStatus\": \"{newStatus}\", \"Reason\": \"{reason}\" }}";
        var stateEvent = new PositionEvent(position.Id, eventType, payload);
        position.Events.Add(stateEvent);

        _logger.LogInformation("PositionStateChanged: Changed position {PositionId} status from {OldStatus} to {NewStatus}. Reason: {Reason}",
            id, oldStatus, newStatus, reason);

        _logger.LogInformation("PositionEventCreated: Created event {EventType} for PositionId: {PositionId}", stateEvent.EventType, position.Id);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task AddPositionEventAsync(Guid positionId, string eventType, string payload, CancellationToken cancellationToken = default)
    {
        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            throw new DomainException($"Position with ID {positionId} not found.");
        }

        var positionEvent = new PositionEvent(positionId, eventType, payload);
        position.Events.Add(positionEvent);

        _logger.LogInformation("PositionEventCreated: Manually created event {EventType} for PositionId: {PositionId}", eventType, positionId);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
