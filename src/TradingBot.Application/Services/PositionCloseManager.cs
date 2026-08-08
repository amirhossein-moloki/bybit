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

public class PositionCloseManager : IPositionCloseManager
{
    private readonly IPositionRepository _positionRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IExchangeTradingGateway _exchangeGateway;
    private readonly IPnLCalculator _pnlCalculator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PositionCloseManager> _logger;

    public PositionCloseManager(
        IPositionRepository positionRepository,
        ITradeRepository tradeRepository,
        IExchangeTradingGateway exchangeGateway,
        IPnLCalculator pnlCalculator,
        IUnitOfWork unitOfWork,
        ILogger<PositionCloseManager> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _tradeRepository = tradeRepository ?? throw new ArgumentNullException(nameof(tradeRepository));
        _exchangeGateway = exchangeGateway ?? throw new ArgumentNullException(nameof(exchangeGateway));
        _pnlCalculator = pnlCalculator ?? throw new ArgumentNullException(nameof(pnlCalculator));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ClosePositionAsync(
        Guid positionId,
        CloseReason reason,
        decimal? exitPrice = null,
        string source = "System",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ClosePositionStarted: PositionId={PositionId}, Reason={Reason}, Price={Price}, Source={Source}",
            positionId, reason, exitPrice, source);

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            throw new DomainException($"Position with ID {positionId} not found.");
        }

        if (position.Status == PositionStatus.Closed || position.Status == PositionStatus.Liquidated)
        {
            _logger.LogWarning("ClosePositionSkipped: Position {PositionId} is already in {Status} state.", positionId, position.Status);
            return true;
        }

        // Submit order to Exchange to close the rest of the position (except if it is a Liquidation, where exchange already liquidated)
        var remainingQty = position.RemainingQuantity;
        var execPrice = exitPrice ?? position.CurrentPrice;
        var execFee = 0m;

        if (remainingQty > 0 && reason != CloseReason.Liquidation)
        {
            var oppositeSide = position.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            var clientOrderId = $"BOT-CLOSE-{Guid.NewGuid():N}";
            var request = new OrderRequest
            {
                Symbol = position.Symbol,
                Side = oppositeSide,
                Type = exitPrice.HasValue ? OrderType.Limit : OrderType.Market,
                Quantity = remainingQty,
                Price = exitPrice ?? 0m,
                ClientOrderId = clientOrderId,
                ReduceOnly = true
            };

            _logger.LogInformation("ClosePositionExchangeSubmission: Submitting close order to exchange for Symbol={Symbol}, Qty={Qty}",
                position.Symbol, remainingQty);

            var result = await _exchangeGateway.CreateOrderAsync(request, cancellationToken);
            if (!result.Success)
            {
                _logger.LogError("ClosePositionExchangeFailed: Exchange rejected close order. Error={Error}", result.ErrorMessage);
                return false;
            }

            execPrice = result.ExecutedPrice > 0 ? result.ExecutedPrice : (exitPrice ?? position.CurrentPrice);
        }

        // Emit PositionClosing
        var closingEvent = new PositionEvent(position.Id, "PositionClosing", $"{{ \"Reason\": \"{reason}\", \"Price\": {execPrice} }}");
        position.Events.Add(closingEvent);

        if (reason == CloseReason.Liquidation)
        {
            position.Liquidate();
        }
        else
        {
            position.Close(execPrice, execFee);
        }

        await SettleAndCreateTradeResultAsync(position, reason, cancellationToken);

        _logger.LogInformation("ClosePositionCompleted: Successfully closed PositionId={PositionId} on exchange and DB.", position.Id);
        return true;
    }

    public async Task<bool> HandleExchangePositionUpdateAsync(
        string symbol,
        decimal exchangeQuantity,
        decimal exitPrice,
        decimal fee,
        CloseReason reason,
        string? rawEventDetails = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("HandleExchangePositionUpdateStarted: Symbol={Symbol}, Qty={Qty}, Price={Price}, Reason={Reason}",
            symbol, exchangeQuantity, exitPrice, reason);

        if (exchangeQuantity != 0)
        {
            return true; // We only care about complete closure (Quantity == 0)
        }

        // Find open position in DB
        var openPositions = await _positionRepository.GetOpenPositionsAsync(cancellationToken);
        var positionToClose = openPositions.FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (positionToClose == null)
        {
            _logger.LogInformation("HandleExchangePositionUpdateSkipped: No open position found in database for Symbol={Symbol}.", symbol);
            return true;
        }

        // Load full position with events
        var position = await _positionRepository.GetByIdAsync(positionToClose.Id, cancellationToken);
        if (position == null || position.Status == PositionStatus.Closed || position.Status == PositionStatus.Liquidated)
        {
            return true;
        }

        _logger.LogInformation("HandleExchangePositionUpdateExecuting: Closing position {PositionId} based on exchange quantity zero.", position.Id);

        // Emit PositionClosing
        var closingEvent = new PositionEvent(position.Id, "PositionClosing", $"{{ \"Reason\": \"{reason}\", \"Price\": {exitPrice} }}");
        position.Events.Add(closingEvent);

        if (reason == CloseReason.Liquidation)
        {
            position.Liquidate();
        }
        else
        {
            position.Close(exitPrice, fee);
        }

        await SettleAndCreateTradeResultAsync(position, reason, cancellationToken);

        return true;
    }

    private async Task SettleAndCreateTradeResultAsync(Position position, CloseReason reason, CancellationToken cancellationToken)
    {
        var grossPnL = position.RealizedPnL;
        var tradingFee = position.Fee;
        var fundingFee = 0m; // Retrieve or assume 0m if not available
        var netPnL = _pnlCalculator.CalculateNetPnL(grossPnL, tradingFee, fundingFee);

        // Calculate weighted exit price based on Gross PnL
        decimal exitPrice = position.CurrentPrice;
        if (position.Quantity > 0)
        {
            if (position.Side == OrderSide.Buy)
            {
                exitPrice = position.EntryPrice + (grossPnL / position.Quantity);
            }
            else
            {
                exitPrice = position.EntryPrice - (grossPnL / position.Quantity);
            }
        }

        if (exitPrice <= 0)
        {
            exitPrice = 0.00000001m; // Ensure positive price for Trade constructor check
        }

        var trade = new Trade(
            positionId: position.Id,
            entryPrice: position.EntryPrice,
            exitPrice: exitPrice,
            quantity: position.Quantity,
            grossPnL: grossPnL,
            tradingFee: tradingFee,
            fundingFee: fundingFee,
            netPnL: netPnL,
            closeReason: reason,
            openedAt: position.OpenedAt,
            closedAt: position.ClosedAt ?? DateTime.UtcNow
        );

        await _tradeRepository.SaveAsync(trade, cancellationToken);

        var eventType = reason == CloseReason.Liquidation ? "PositionLiquidated" : "PositionClosed";
        var payload = $"{{ \"PositionId\": \"{position.Id}\", \"Symbol\": \"{position.Symbol}\", \"Side\": \"{position.Side}\", \"Quantity\": {position.Quantity}, \"GrossPnL\": {grossPnL}, \"TradingFee\": {tradingFee}, \"NetPnL\": {netPnL}, \"CloseReason\": \"{reason}\", \"Timestamp\": \"{DateTime.UtcNow:O}\" }}";
        var posEvent = new PositionEvent(position.Id, eventType, payload);
        position.Events.Add(posEvent);

        _positionRepository.Update(position);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PositionSettleAndTradeResultCompleted: Created Trade record and recorded event {EventType} for PositionId={PositionId}.",
            eventType, position.Id);
    }
}
