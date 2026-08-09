using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Application.Services;

public class StopLossManager : IStopLossManager
{
    private readonly IPositionRepository _positionRepository;
    private readonly IExchangeTradingGateway _exchangeGateway;
    private readonly IExchangeInstrumentRules _instrumentRules;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StopLossManager> _logger;

    public StopLossManager(
        IPositionRepository positionRepository,
        IExchangeTradingGateway exchangeGateway,
        IExchangeInstrumentRules instrumentRules,
        IUnitOfWork unitOfWork,
        ILogger<StopLossManager> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _exchangeGateway = exchangeGateway ?? throw new ArgumentNullException(nameof(exchangeGateway));
        _instrumentRules = instrumentRules ?? throw new ArgumentNullException(nameof(instrumentRules));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> UpdateStopLossAsync(
        Guid positionId,
        decimal? stopLoss,
        string reason = "Update",
        string source = "System",
        CancellationToken cancellationToken = default)
    {
        using var @lock = await PositionLockManager.AcquireLockAsync(positionId, TimeSpan.FromSeconds(5), cancellationToken);

        _logger.LogInformation("StopLossUpdateStarted: PositionId={PositionId}, SL={StopLoss}, Source={Source}, Reason={Reason}",
            positionId, stopLoss, source, reason);

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            _logger.LogWarning("StopLossUpdateFailed: Position with ID {PositionId} not found.", positionId);
            throw new DomainException($"Position with ID {positionId} not found.");
        }

        if (position.Status != PositionStatus.Open && position.Status != PositionStatus.PartiallyClosed)
        {
            _logger.LogWarning("StopLossUpdateFailed: Position {PositionId} is not in Open or PartiallyClosed state. Current status is {Status}.",
                positionId, position.Status);
            throw new DomainException($"Cannot update Stop Loss on a position with status {position.Status}.");
        }

        // 1. Validate Side-based constraints
        if (stopLoss.HasValue)
        {
            if (stopLoss.Value <= 0)
            {
                throw new DomainException("StopLoss price must be greater than zero.");
            }

            var isBreakEvenOrTrailing = reason == "Break-Even" || reason == "Trailing Stop" || reason == "BreakEvenActivated" || (reason != null && (reason.Contains("Trailing") || reason.Contains("Break")));

            if (position.Side == OrderSide.Buy) // LONG
            {
                if (!isBreakEvenOrTrailing && stopLoss.Value >= position.EntryPrice)
                {
                    _logger.LogWarning("StopLossUpdateRejected: For LONG position, StopLoss ({SL}) must be less than EntryPrice ({Entry}).",
                        stopLoss.Value, position.EntryPrice);
                    throw new DomainException($"Invalid StopLoss: For LONG position, StopLoss ({stopLoss.Value}) must be less than EntryPrice ({position.EntryPrice}).");
                }

                if (position.StopLoss.HasValue && stopLoss.Value < position.StopLoss.Value)
                {
                    _logger.LogWarning("StopLossUpdateRejected: Cannot move StopLoss backwards from {OldSL} to {NewSL} for LONG.",
                        position.StopLoss.Value, stopLoss.Value);
                    throw new DomainException($"Invalid StopLoss: Cannot move StopLoss backwards from {position.StopLoss.Value} to {stopLoss.Value} for LONG.");
                }
            }
            else if (position.Side == OrderSide.Sell) // SHORT
            {
                if (!isBreakEvenOrTrailing && stopLoss.Value <= position.EntryPrice)
                {
                    _logger.LogWarning("StopLossUpdateRejected: For SHORT position, StopLoss ({SL}) must be greater than EntryPrice ({Entry}).",
                        stopLoss.Value, position.EntryPrice);
                    throw new DomainException($"Invalid StopLoss: For SHORT position, StopLoss ({stopLoss.Value}) must be greater than EntryPrice ({position.EntryPrice}).");
                }

                if (position.StopLoss.HasValue && stopLoss.Value > position.StopLoss.Value)
                {
                    _logger.LogWarning("StopLossUpdateRejected: Cannot move StopLoss backwards from {OldSL} to {NewSL} for SHORT.",
                        position.StopLoss.Value, stopLoss.Value);
                    throw new DomainException($"Invalid StopLoss: Cannot move StopLoss backwards from {position.StopLoss.Value} to {stopLoss.Value} for SHORT.");
                }
            }

            // 2. Validate against precision / tick size rules
            var rules = _instrumentRules.GetInstrumentRules(position.Symbol);
            if (rules != null)
            {
                // Check price precision
                var rounded = Math.Round(stopLoss.Value, rules.PricePrecision);
                if (rounded != stopLoss.Value)
                {
                    _logger.LogWarning("StopLossUpdateRejected: StopLoss ({SL}) does not match symbol price precision ({Precision}).",
                        stopLoss.Value, rules.PricePrecision);
                    throw new DomainException($"StopLoss ({stopLoss.Value}) exceeds the allowed price precision of {rules.PricePrecision} decimal places.");
                }

                // Check tick size alignment
                if (rules.TickSize > 0)
                {
                    var remainder = stopLoss.Value % rules.TickSize;
                    if (remainder != 0 && Math.Abs(remainder - rules.TickSize) > 0.00000001m)
                    {
                        _logger.LogWarning("StopLossUpdateRejected: StopLoss ({SL}) is not a multiple of TickSize ({TickSize}).",
                            stopLoss.Value, rules.TickSize);
                        throw new DomainException($"StopLoss ({stopLoss.Value}) must be a multiple of the allowed Tick Size ({rules.TickSize}).");
                    }
                }
            }
        }

        var oldStopLoss = position.StopLoss;

        // 3. Send update to the Exchange (Bybit)
        _logger.LogInformation("StopLossExchangeSubmission: Sending SetTradingStop to exchange for Symbol={Symbol}, SL={StopLoss}",
            position.Symbol, stopLoss);

        var exchangeResult = await _exchangeGateway.SetTradingStopAsync(
            position.Symbol,
            position.Side,
            stopLoss,
            position.TakeProfit,
            cancellationToken);

        if (!exchangeResult.Success)
        {
            _logger.LogError("StopLossExchangeFailed: Exchange rejected SetTradingStop. Error={Error}, Code={Code}",
                exchangeResult.ErrorMessage, exchangeResult.ErrorCode);
            return false;
        }

        // 4. Update the local state in DB since Exchange operation succeeded
        position.UpdateStopLoss(stopLoss, reason ?? "Update", source ?? "System");

        var eventType = !oldStopLoss.HasValue && stopLoss.HasValue ? "StopLossCreated" :
                        oldStopLoss.HasValue && !stopLoss.HasValue ? "StopLossRemoved" : "StopLossUpdated";

        var payload = $"{{ \"PositionId\": \"{position.Id}\", \"OldStopLoss\": {(oldStopLoss.HasValue ? oldStopLoss.Value.ToString() : "null")}, \"NewStopLoss\": {(stopLoss.HasValue ? stopLoss.Value.ToString() : "null")}, \"Reason\": \"{reason ?? "Update"}\", \"Source\": \"{source ?? "System"}\" }}";
        var posEvent = new PositionEvent(position.Id, eventType, payload);
        position.Events.Add(posEvent);

        _logger.LogInformation("StopLossEventCreated: Created event {EventType} for PositionId: {PositionId}",
            eventType, position.Id);

        _positionRepository.Update(position);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("StopLossUpdateCompleted: Successfully updated SL on exchange and database for PositionId={PositionId}.",
            position.Id);

        return true;
    }
}
