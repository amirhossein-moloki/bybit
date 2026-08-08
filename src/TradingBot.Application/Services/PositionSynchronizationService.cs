using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Services;

public class PositionSynchronizationService : IPositionSynchronizationService
{
    private static readonly SemaphoreSlim _syncSemaphore = new(1, 1);

    private readonly IPositionRepository _positionRepository;
    private readonly IPositionGateway _positionGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PositionSynchronizationService> _logger;

    public PositionSynchronizationService(
        IPositionRepository positionRepository,
        IPositionGateway positionGateway,
        IUnitOfWork unitOfWork,
        ILogger<PositionSynchronizationService> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _positionGateway = positionGateway ?? throw new ArgumentNullException(nameof(positionGateway));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Position Sync Started: Acquiring lock for position synchronization...");

        if (!await _syncSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            _logger.LogWarning("Position Sync Timeout: Could not acquire synchronization lock within timeout.");
            return;
        }

        try
        {
            // 1. Load open database positions (returns untracked entities)
            var dbPositions = (await _positionRepository.GetOpenPositionsAsync(cancellationToken)).ToList();
            _logger.LogInformation("Position Sync: Loaded {Count} open positions from database.", dbPositions.Count);

            // 2. Load open positions from exchange
            var exchangePositions = await _positionGateway.GetOpenPositionsAsync();
            _logger.LogInformation("Position Sync: Loaded {Count} open positions from exchange.", exchangePositions.Count);

            var exchangeDict = exchangePositions.ToDictionary(
                p => (p.Symbol.ToUpperInvariant(), p.Side),
                p => p
            );

            bool hasChanges = false;

            // 3. Compare and Update
            foreach (var dbPos in dbPositions)
            {
                var sideMapped = dbPos.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
                var key = (dbPos.Symbol.ToUpperInvariant(), sideMapped);

                if (exchangeDict.TryGetValue(key, out var exPos))
                {
                    // Existing Position - Update values
                    dbPos.UpdateFromExchange(
                        quantity: exPos.Quantity,
                        entryPrice: exPos.EntryPrice,
                        markPrice: exPos.MarkPrice,
                        leverage: exPos.Leverage,
                        margin: exPos.Margin,
                        unrealizedPnL: exPos.UnrealizedPnL,
                        exchangePositionId: exPos.ExchangePositionId
                    );

                    dbPos.MarkDesynchronized(false); // sync successful

                    _logger.LogInformation("Position Updated: PositionId={PositionId}, Symbol={Symbol}, Side={Side} updated to match exchange.",
                        dbPos.Id, dbPos.Symbol, dbPos.Side);

                    _positionRepository.Update(dbPos);
                    hasChanges = true;
                }
                else
                {
                    // Position closed on exchange but open in DB
                    _logger.LogWarning("Position Sync Mismatch: PositionId={PositionId}, Symbol={Symbol}, Side={Side} is open in DB but missing/closed on exchange.",
                        dbPos.Id, dbPos.Symbol, dbPos.Side);

                    dbPos.Close(dbPos.CurrentPrice, 0m);
                    dbPos.MarkDesynchronized(false); // Resolved by closing

                    _logger.LogInformation("Position Reconciled: PositionId={PositionId} marked as Closed in database to match exchange.", dbPos.Id);

                    _positionRepository.Update(dbPos);
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Position Sync Saved: Database changes persisted successfully.");
            }
            else
            {
                _logger.LogInformation("Position Sync Completed: No differences found.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Position Sync Failed: Exception occurred during synchronization.");
            throw;
        }
        finally
        {
            _syncSemaphore.Release();
        }
    }
}
