using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Mappers;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.Application.Services;

public class PositionReconciliationService : IPositionReconciliationService
{
    private static readonly SemaphoreSlim _reconcileSemaphore = new(1, 1);

    private readonly IPositionRepository _positionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPositionGateway _positionGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PositionReconciliationService> _logger;

    public PositionReconciliationService(
        IPositionRepository positionRepository,
        IOrderRepository orderRepository,
        IPositionGateway positionGateway,
        IUnitOfWork unitOfWork,
        ILogger<PositionReconciliationService> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _positionGateway = positionGateway ?? throw new ArgumentNullException(nameof(positionGateway));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Position Reconciliation Started: Acquiring lock...");

        if (!await _reconcileSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            _logger.LogWarning("Position Reconciliation Timeout: Could not acquire lock within timeout.");
            return;
        }

        try
        {
            // 1. Fetch DB positions (returns untracked entities) and Exchange positions
            var dbPositions = (await _positionRepository.GetOpenPositionsAsync(cancellationToken)).ToList();
            var exchangePositions = await _positionGateway.GetOpenPositionsAsync();

            var dbDict = dbPositions.ToDictionary(
                p => (p.Symbol.ToUpperInvariant(), p.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short),
                p => p
            );

            var exchangeDict = exchangePositions.ToDictionary(
                p => (p.Symbol.ToUpperInvariant(), p.Side),
                p => p
            );

            bool hasChanges = false;

            // Rule 1 & 2: Process Open DB Positions
            foreach (var dbPos in dbPositions)
            {
                var sideMapped = dbPos.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
                var key = (dbPos.Symbol.ToUpperInvariant(), sideMapped);

                if (exchangeDict.TryGetValue(key, out var exPos))
                {
                    // Check for mismatches
                    if (dbPos.Quantity != exPos.Quantity || dbPos.EntryPrice != exPos.EntryPrice)
                    {
                        _logger.LogWarning("Position Mismatch Detected: DB PositionId={Id}, Symbol={Symbol}, Side={Side} has Quantity={DbQty}, EntryPrice={DbPrice} but Exchange has Quantity={ExQty}, EntryPrice={ExPrice}.",
                            dbPos.Id, dbPos.Symbol, dbPos.Side, dbPos.Quantity, dbPos.EntryPrice, exPos.Quantity, exPos.EntryPrice);

                        // Mark as desynchronized
                        dbPos.MarkDesynchronized(true);

                        // Immediately repair utilizing Exchange as Source Of Truth
                        dbPos.UpdateFromExchange(
                            quantity: exPos.Quantity,
                            entryPrice: exPos.EntryPrice,
                            markPrice: exPos.MarkPrice,
                            leverage: exPos.Leverage,
                            margin: exPos.Margin,
                            unrealizedPnL: exPos.UnrealizedPnL,
                            exchangePositionId: exPos.ExchangePositionId
                        );

                        // Mark synchronized again after repair
                        dbPos.MarkDesynchronized(false);

                        _logger.LogInformation("Position Reconciled: PositionId={Id} successfully repaired to match exchange.", dbPos.Id);

                        _positionRepository.Update(dbPos);
                        hasChanges = true;
                    }
                    else
                    {
                        // Clean sync of other optional/mark price fields (e.g. CurrentPrice / MarkPrice)
                        dbPos.UpdateFromExchange(
                            quantity: exPos.Quantity,
                            entryPrice: exPos.EntryPrice,
                            markPrice: exPos.MarkPrice,
                            leverage: exPos.Leverage,
                            margin: exPos.Margin,
                            unrealizedPnL: exPos.UnrealizedPnL,
                            exchangePositionId: exPos.ExchangePositionId
                        );
                        dbPos.MarkDesynchronized(false);

                        _positionRepository.Update(dbPos);
                        hasChanges = true; // Always save clean updates too!
                    }
                }
                else
                {
                    // Exchange has closed/missing position
                    _logger.LogWarning("Position Sync Mismatch: PositionId={PositionId}, Symbol={Symbol}, Side={Side} is open in DB but missing on exchange.",
                        dbPos.Id, dbPos.Symbol, dbPos.Side);

                    dbPos.MarkDesynchronized(true);

                    // Reconcile DB position to Closed to match exchange
                    dbPos.Close(dbPos.CurrentPrice, 0m);
                    dbPos.MarkDesynchronized(false);

                    _logger.LogInformation("Position Reconciled: PositionId={PositionId} marked as Closed in database to match exchange.", dbPos.Id);

                    _positionRepository.Update(dbPos);
                    hasChanges = true;
                }
            }

            // Rule 3: Database Position Missing (Exchange has open position, DB does not)
            foreach (var exPos in exchangePositions)
            {
                var dbSide = exPos.Side == PositionSide.Long ? OrderSide.Buy : OrderSide.Sell;
                var key = (exPos.Symbol.ToUpperInvariant(), exPos.Side);

                if (!dbDict.ContainsKey(key))
                {
                    _logger.LogWarning("Exchange Position Missing: Symbol={Symbol}, Side={Side} has open position on exchange but is missing in DB.",
                        exPos.Symbol, exPos.Side);

                    // Create Recovery Record: Unknown Position requires a valid order first to satisfy DB Foreign Key
                    var placeholderOrderId = Guid.NewGuid();
                    var placeholderOrder = new Order(
                        id: placeholderOrderId,
                        clientOrderId: "REC-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpperInvariant(),
                        symbol: new Symbol(exPos.Symbol),
                        side: dbSide,
                        type: OrderType.Market,
                        quantity: new Quantity(exPos.Quantity),
                        price: new Money(exPos.EntryPrice)
                    );
                    placeholderOrder.Submit();
                    placeholderOrder.Accept(exPos.ExchangePositionId ?? "REC-EX-ID");
                    placeholderOrder.MarkFilled();

                    await _orderRepository.AddAsync(placeholderOrder, cancellationToken);

                    var recoveredPos = ExchangePositionMapper.ToDomain(exPos, placeholderOrderId);

                    // Consistent audit trails
                    var payload = $"{{\"Message\": \"Unknown Position recovered from exchange\", \"ExchangePositionId\": \"{exPos.ExchangePositionId}\", \"Symbol\": \"{exPos.Symbol}\", \"Side\": \"{exPos.Side}\", \"Quantity\": {exPos.Quantity}, \"EntryPrice\": {exPos.EntryPrice}}}";
                    var auditEvent = new PositionEvent(recoveredPos.Id, "PositionRecovered", payload);
                    recoveredPos.Events.Add(auditEvent);

                    await _positionRepository.AddAsync(recoveredPos, cancellationToken);

                    _logger.LogInformation("Recovery Completed: Created Unknown Position recovery record. PositionId={PositionId}, OrderId={OrderId}, Symbol={Symbol}",
                        recoveredPos.Id, placeholderOrderId, recoveredPos.Symbol);

                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Position Reconciliation Saved: Database changes persisted successfully.");
            }
            else
            {
                _logger.LogInformation("Position Reconciliation Completed: No state mismatches detected.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Position Reconciliation Failed: Exception occurred during reconciliation.");
            throw;
        }
        finally
        {
            _reconcileSemaphore.Release();
        }
    }
}
