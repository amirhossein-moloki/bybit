using System;
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

public class TrailingStopManager : ITrailingStopManager
{
    private readonly IPositionRepository _positionRepository;
    private readonly IStopLossManager _stopLossManager;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TrailingStopManager> _logger;

    public TrailingStopManager(
        IPositionRepository positionRepository,
        IStopLossManager stopLossManager,
        IUnitOfWork unitOfWork,
        ILogger<TrailingStopManager> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _stopLossManager = stopLossManager ?? throw new ArgumentNullException(nameof(stopLossManager));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteTrailingStopCheckAsync(
        Guid positionId,
        decimal currentPrice,
        TrailingStopSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var @lock = await PositionLockManager.AcquireLockAsync(positionId, TimeSpan.FromSeconds(5), cancellationToken);

        if (settings == null || !settings.Enabled)
        {
            return false;
        }

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            _logger.LogWarning("TrailingStopCheckFailed: Position {PositionId} not found.", positionId);
            return false;
        }

        if (position.Status != PositionStatus.Open && position.Status != PositionStatus.PartiallyClosed)
        {
            return false;
        }

        // Determine if active
        if (settings.ActivationPrice.HasValue)
        {
            if (position.Side == OrderSide.Buy) // LONG
            {
                if (currentPrice < settings.ActivationPrice.Value)
                {
                    return false;
                }
            }
            else // SHORT
            {
                if (currentPrice > settings.ActivationPrice.Value)
                {
                    return false;
                }
            }
        }

        // Calculate desired Stop Loss
        decimal desiredSL;
        if (settings.Distance.HasValue)
        {
            if (position.Side == OrderSide.Buy)
            {
                desiredSL = currentPrice - settings.Distance.Value;
            }
            else
            {
                desiredSL = currentPrice + settings.Distance.Value;
            }
        }
        else if (settings.Percentage.HasValue)
        {
            if (position.Side == OrderSide.Buy)
            {
                desiredSL = currentPrice * (1m - settings.Percentage.Value / 100m);
            }
            else
            {
                desiredSL = currentPrice * (1m + settings.Percentage.Value / 100m);
            }
        }
        else
        {
            return false;
        }

        // Validate improvement meets trailing step
        if (position.StopLoss.HasValue)
        {
            if (position.Side == OrderSide.Buy)
            {
                // Improvement must be at least settings.Step
                if (desiredSL - position.StopLoss.Value < settings.Step)
                {
                    return false;
                }
            }
            else
            {
                // Improvement must be at least settings.Step
                if (position.StopLoss.Value - desiredSL < settings.Step)
                {
                    return false;
                }
            }
        }

        // Double check no backwards movement / wrong direction
        if (position.Side == OrderSide.Buy)
        {
            if (position.StopLoss.HasValue && desiredSL < position.StopLoss.Value)
            {
                return false;
            }
            if (desiredSL > currentPrice)
            {
                return false;
            }
        }
        else
        {
            if (position.StopLoss.HasValue && desiredSL > position.StopLoss.Value)
            {
                return false;
            }
            if (desiredSL < currentPrice)
            {
                return false;
            }
        }

        _logger.LogInformation("TrailingStopTriggered: PositionId={PositionId}, Side={Side}, DesiredSL={DesiredSL}", positionId, position.Side, desiredSL);

        // Update stop loss on exchange
        var success = await _stopLossManager.UpdateStopLossAsync(positionId, desiredSL, "Trailing Stop", "System", cancellationToken);
        if (!success)
        {
            return false;
        }

        // Record TrailingStopUpdated Event
        var payload = $"{{\"PositionId\": \"{position.Id}\", \"CurrentPrice\": {currentPrice}, \"NewStopLoss\": {desiredSL}, \"Step\": {settings.Step}}}";
        var tsEvent = new PositionEvent(position.Id, "TrailingStopUpdated", payload);

        var updatedPosition = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (updatedPosition != null)
        {
            updatedPosition.Events.Add(tsEvent);
            _positionRepository.Update(updatedPosition);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
