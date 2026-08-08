using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Application.Services;

public class BreakEvenManager : IBreakEvenManager
{
    private readonly IPositionRepository _positionRepository;
    private readonly IStopLossManager _stopLossManager;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BreakEvenManager> _logger;

    public BreakEvenManager(
        IPositionRepository positionRepository,
        IStopLossManager stopLossManager,
        IUnitOfWork unitOfWork,
        ILogger<BreakEvenManager> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _stopLossManager = stopLossManager ?? throw new ArgumentNullException(nameof(stopLossManager));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteBreakEvenCheckAsync(
        Guid positionId,
        decimal currentPrice,
        BreakEvenSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (settings == null || !settings.Enabled)
        {
            return false;
        }

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            _logger.LogWarning("BreakEvenCheckFailed: Position {PositionId} not found.", positionId);
            return false;
        }

        if (position.Status != PositionStatus.Open && position.Status != PositionStatus.PartiallyClosed)
        {
            return false;
        }

        // Break-Even should be activated only once.
        var alreadyActivated = position.Events.Any(e => e.EventType == "BreakEvenActivated");
        if (alreadyActivated)
        {
            return false;
        }

        bool isTriggered = false;

        switch (settings.TriggerType)
        {
            case BreakEvenTriggerType.Price:
                if (position.Side == OrderSide.Buy)
                {
                    isTriggered = currentPrice >= settings.TriggerValue;
                }
                else
                {
                    isTriggered = currentPrice <= settings.TriggerValue;
                }
                break;

            case BreakEvenTriggerType.Percentage:
                if (position.Side == OrderSide.Buy)
                {
                    isTriggered = currentPrice >= position.EntryPrice * (1m + settings.TriggerValue / 100m);
                }
                else
                {
                    isTriggered = currentPrice <= position.EntryPrice * (1m - settings.TriggerValue / 100m);
                }
                break;

            case BreakEvenTriggerType.RMultiple:
                var initialSL = position.StopLossHistories.OrderBy(h => h.CreatedAt).FirstOrDefault()?.OldPrice ?? position.StopLoss;
                if (initialSL.HasValue && initialSL.Value > 0 && initialSL.Value != position.EntryPrice)
                {
                    var r = Math.Abs(position.EntryPrice - initialSL.Value);
                    if (position.Side == OrderSide.Buy)
                    {
                        isTriggered = currentPrice >= position.EntryPrice + r * settings.TriggerValue;
                    }
                    else
                    {
                        isTriggered = currentPrice <= position.EntryPrice - r * settings.TriggerValue;
                    }
                }
                break;
        }

        if (!isTriggered)
        {
            return false;
        }

        // Calculate Break-Even Stop Loss
        decimal newSL;
        if (position.Side == OrderSide.Buy) // LONG
        {
            newSL = position.EntryPrice + settings.Offset;

            // Validate: New SL must be >= Existing SL, and New SL <= Current Price
            if (position.StopLoss.HasValue && newSL < position.StopLoss.Value)
            {
                _logger.LogWarning("BreakEvenRejected: Calculated SL {NewSL} is worse than current SL {OldSL} for LONG.", newSL, position.StopLoss.Value);
                return false;
            }
            if (newSL > currentPrice)
            {
                _logger.LogWarning("BreakEvenRejected: Calculated SL {NewSL} is above current price {Price} for LONG.", newSL, currentPrice);
                return false;
            }
        }
        else // SHORT
        {
            newSL = position.EntryPrice - settings.Offset;

            // Validate: New SL must be <= Existing SL, and New SL >= Current Price
            if (position.StopLoss.HasValue && newSL > position.StopLoss.Value)
            {
                _logger.LogWarning("BreakEvenRejected: Calculated SL {NewSL} is worse than current SL {OldSL} for SHORT.", newSL, position.StopLoss.Value);
                return false;
            }
            if (newSL < currentPrice)
            {
                _logger.LogWarning("BreakEvenRejected: Calculated SL {NewSL} is below current price {Price} for SHORT.", newSL, currentPrice);
                return false;
            }
        }

        _logger.LogInformation("BreakEvenTriggered: PositionId={PositionId}, Side={Side}, NewSL={NewSL}", positionId, position.Side, newSL);

        // Update SL
        var success = await _stopLossManager.UpdateStopLossAsync(positionId, newSL, "Break-Even", "System", cancellationToken);
        if (!success)
        {
            return false;
        }

        // Record BreakEvenActivated Event
        var payload = $"{{\"PositionId\": \"{position.Id}\", \"TriggerPrice\": {currentPrice}, \"NewStopLoss\": {newSL}, \"Offset\": {settings.Offset}}}";
        var beEvent = new PositionEvent(position.Id, "BreakEvenActivated", payload);

        // Reload position to attach to current unit of work context correctly
        var updatedPosition = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (updatedPosition != null)
        {
            updatedPosition.Events.Add(beEvent);
            _positionRepository.Update(updatedPosition);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
