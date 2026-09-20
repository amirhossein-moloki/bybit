using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Application.Services;

public class PositionManagementHandler : IPositionManagementHandler
{
    private readonly IPositionRepository _positionRepository;
    private readonly IStopLossManager _stopLossManager;
    private readonly IBreakEvenManager _breakEvenManager;
    private readonly IPartialCloseManager _partialCloseManager;
    private readonly IPositionCloseManager _positionCloseManager;
    private readonly IMessageAnalysisRepository _analysisRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PositionManagementHandler> _logger;

    public const decimal MinimumConfidenceThreshold = 0.70m;

    public PositionManagementHandler(
        IPositionRepository positionRepository,
        IStopLossManager stopLossManager,
        IBreakEvenManager breakEvenManager,
        IPartialCloseManager partialCloseManager,
        IPositionCloseManager positionCloseManager,
        IMessageAnalysisRepository analysisRepository,
        IUnitOfWork unitOfWork,
        ILogger<PositionManagementHandler> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _stopLossManager = stopLossManager ?? throw new ArgumentNullException(nameof(stopLossManager));
        _breakEvenManager = breakEvenManager ?? throw new ArgumentNullException(nameof(breakEvenManager));
        _partialCloseManager = partialCloseManager ?? throw new ArgumentNullException(nameof(partialCloseManager));
        _positionCloseManager = positionCloseManager ?? throw new ArgumentNullException(nameof(positionCloseManager));
        _analysisRepository = analysisRepository ?? throw new ArgumentNullException(nameof(analysisRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PositionManagementResult> ProcessPositionManagementAsync(
        MessageAnalysis analysis,
        TelegramMessage message,
        CancellationToken cancellationToken = default)
    {
        if (analysis == null) throw new ArgumentNullException(nameof(analysis));
        if (message == null) throw new ArgumentNullException(nameof(message));

        _logger.LogInformation("PositionManagementWorkflowStarted: MessageId={MessageId}, Intent={Intent}, Action={Action}, Symbol={Symbol}, Confidence={Confidence}",
            message.MessageId, analysis.Intent, analysis.Action, analysis.TargetSymbol, analysis.ConfidenceScore);

        // Safety Rule 1: Confidence Threshold Check
        if (analysis.ConfidenceScore < MinimumConfidenceThreshold)
        {
            var auditReason = $"Confidence score {analysis.ConfidenceScore:P0} is below configured minimum threshold {MinimumConfidenceThreshold:P0}.";
            _logger.LogWarning("PositionManagementSafetyRejected: {AuditReason} MessageId={MessageId}", auditReason, message.MessageId);

            analysis.UpdateProcessingStatus("Filtered", auditReason);
            _analysisRepository.Update(analysis);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PositionManagementResult(false, "Filtered", auditReason, Symbol: analysis.TargetSymbol, ExecutedAction: analysis.Action);
        }

        // Safety Rule 2: Symbol Identification Check
        var action = analysis.Action ?? string.Empty;
        var isGlobalAction = action.Equals("CloseAllPositions", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(analysis.TargetSymbol) && !isGlobalAction)
        {
            var auditReason = "Symbol cannot be identified from message content or context.";
            _logger.LogWarning("PositionManagementSafetyRejected: {AuditReason} MessageId={MessageId}", auditReason, message.MessageId);

            analysis.UpdateProcessingStatus("Filtered", auditReason);
            _analysisRepository.Update(analysis);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PositionManagementResult(false, "Filtered", auditReason, ExecutedAction: action);
        }

        // Safety Rule 3: Find Matching Active Position
        var openPositions = await _positionRepository.GetOpenPositionsAsync(cancellationToken);
        List<Position> matchingPositions;

        if (isGlobalAction)
        {
            matchingPositions = openPositions.ToList();
        }
        else
        {
            matchingPositions = openPositions
                .Where(p => p.Symbol.Equals(analysis.TargetSymbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!matchingPositions.Any())
        {
            var auditReason = !string.IsNullOrWhiteSpace(analysis.TargetSymbol)
                ? $"No matching open position exists for symbol '{analysis.TargetSymbol}'."
                : "No matching open position exists for execution.";

            _logger.LogInformation("PositionManagementNoPositionFound: {AuditReason} MessageId={MessageId}", auditReason, message.MessageId);

            analysis.UpdateProcessingStatus("Ignored", auditReason);
            _analysisRepository.Update(analysis);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PositionManagementResult(false, "Ignored", auditReason, Symbol: analysis.TargetSymbol, ExecutedAction: action);
        }

        // Execution & Risk Validation
        bool anyExecuted = false;
        var executedPositionIds = new List<Guid>();
        var errorMessages = new List<string>();

        foreach (var position in matchingPositions)
        {
            try
            {
                bool success = false;
                switch (action)
                {
                    case "MoveStopLossToEntry":
                        _logger.LogInformation("Executing MoveStopLossToEntry for PositionId={PositionId}, EntryPrice={EntryPrice}",
                            position.Id, position.EntryPrice);
                        success = await _stopLossManager.UpdateStopLossAsync(
                            position.Id,
                            position.EntryPrice,
                            reason: "Move SL to Entry (Telegram)",
                            source: "Telegram",
                            cancellationToken: cancellationToken);
                        break;

                    case "TakePartialProfit":
                        var closeQty = Math.Max(0.001m, Math.Round(position.RemainingQuantity * 0.50m, 3));
                        _logger.LogInformation("Executing TakePartialProfit for PositionId={PositionId}, Qty={Qty}",
                            position.Id, closeQty);
                        success = await _partialCloseManager.ExecutePartialCloseAsync(
                            position.Id,
                            closeQty,
                            price: null,
                            reason: "Take Partial Profit (Telegram)",
                            source: "Telegram",
                            cancellationToken: cancellationToken);
                        break;

                    case "ClosePosition":
                    case "CloseAllPositions":
                        _logger.LogInformation("Executing ClosePosition for PositionId={PositionId}", position.Id);
                        success = await _positionCloseManager.ClosePositionAsync(
                            position.Id,
                            CloseReason.Manual,
                            exitPrice: null,
                            source: "Telegram Exit Command",
                            cancellationToken: cancellationToken);
                        break;

                    case "UpdateStopLoss":
                        _logger.LogInformation("Executing UpdateStopLoss to Entry for PositionId={PositionId}", position.Id);
                        success = await _stopLossManager.UpdateStopLossAsync(
                            position.Id,
                            position.EntryPrice,
                            reason: "Update Stop Loss (Telegram)",
                            source: "Telegram",
                            cancellationToken: cancellationToken);
                        break;

                    default:
                        _logger.LogWarning("Unknown or unhandled position management action '{Action}' for PositionId={PositionId}",
                            action, position.Id);
                        break;
                }

                if (success)
                {
                    anyExecuted = true;
                    executedPositionIds.Add(position.Id);
                }
                else
                {
                    errorMessages.Add($"Exchange update rejected for position {position.Id}.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing action '{Action}' on position {PositionId}", action, position.Id);
                errorMessages.Add($"Risk/Execution failure on position {position.Id}: {ex.Message}");
            }
        }

        if (anyExecuted)
        {
            var auditReason = $"Successfully executed action '{action}' on position(s) {string.Join(", ", executedPositionIds)}.";
            _logger.LogInformation("PositionManagementWorkflowCompleted: {AuditReason} MessageId={MessageId}", auditReason, message.MessageId);

            analysis.UpdateProcessingStatus("Executed", auditReason);
            _analysisRepository.Update(analysis);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PositionManagementResult(
                true,
                "Executed",
                auditReason,
                PositionId: executedPositionIds.FirstOrDefault(),
                Symbol: analysis.TargetSymbol ?? matchingPositions.FirstOrDefault()?.Symbol,
                ExecutedAction: action);
        }
        else
        {
            var auditReason = errorMessages.Any()
                ? string.Join("; ", errorMessages)
                : $"Action '{action}' failed risk or exchange execution validation.";

            _logger.LogWarning("PositionManagementWorkflowFailed: {AuditReason} MessageId={MessageId}", auditReason, message.MessageId);

            analysis.UpdateProcessingStatus("Failed", auditReason);
            _analysisRepository.Update(analysis);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PositionManagementResult(
                false,
                "Failed",
                auditReason,
                Symbol: analysis.TargetSymbol,
                ExecutedAction: action);
        }
    }
}
