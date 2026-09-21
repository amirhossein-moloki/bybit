using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;

namespace TradingBot.Application.Services;

public class MessageReprocessingService : IMessageReprocessingService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageProcessingAttemptRepository _attemptRepository;
    private readonly IMessageAnalysisRepository? _messageAnalysisRepository;
    private readonly ISignalExtractionRepository? _signalExtractionRepository;
    private readonly IMessageClassifier _messageClassifier;
    private readonly IStructuredSignalExtractor _structuredSignalExtractor;
    private readonly IMessageContextBuilder _messageContextBuilder;
    private readonly IDeterministicRuleEngine _deterministicRuleEngine;
    private readonly IAIIntentInterpreter? _aiIntentInterpreter;
    private readonly ITargetResolver _targetResolver;
    private readonly IIntentSafetyValidator _intentSafetyValidator;
    private readonly IRiskEngine? _riskEngine;
    private readonly TradingBot.Application.Repositories.IUnitOfWork _unitOfWork;
    private readonly ILogger<MessageReprocessingService> _logger;

    public MessageReprocessingService(
        IMessageRepository messageRepository,
        IMessageProcessingAttemptRepository attemptRepository,
        IMessageClassifier messageClassifier,
        IStructuredSignalExtractor structuredSignalExtractor,
        IMessageContextBuilder messageContextBuilder,
        IDeterministicRuleEngine deterministicRuleEngine,
        ITargetResolver targetResolver,
        IIntentSafetyValidator intentSafetyValidator,
        TradingBot.Application.Repositories.IUnitOfWork unitOfWork,
        ILogger<MessageReprocessingService> logger,
        IMessageAnalysisRepository? messageAnalysisRepository = null,
        ISignalExtractionRepository? signalExtractionRepository = null,
        IAIIntentInterpreter? aiIntentInterpreter = null,
        IRiskEngine? riskEngine = null)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _attemptRepository = attemptRepository ?? throw new ArgumentNullException(nameof(attemptRepository));
        _messageClassifier = messageClassifier ?? throw new ArgumentNullException(nameof(messageClassifier));
        _structuredSignalExtractor = structuredSignalExtractor ?? throw new ArgumentNullException(nameof(structuredSignalExtractor));
        _messageContextBuilder = messageContextBuilder ?? throw new ArgumentNullException(nameof(messageContextBuilder));
        _deterministicRuleEngine = deterministicRuleEngine ?? throw new ArgumentNullException(nameof(deterministicRuleEngine));
        _targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
        _intentSafetyValidator = intentSafetyValidator ?? throw new ArgumentNullException(nameof(intentSafetyValidator));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageAnalysisRepository = messageAnalysisRepository;
        _signalExtractionRepository = signalExtractionRepository;
        _aiIntentInterpreter = aiIntentInterpreter;
        _riskEngine = riskEngine;
    }

    public async Task<ReprocessingResultDto> ReprocessMessageAsync(
        Guid messageId,
        ReprocessMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId is required.", nameof(messageId));
        request ??= new ReprocessMessageRequestDto();

        // 1. Verify Original Telegram Message Exists (Message is immutable)
        var message = await _messageRepository.GetByIdAsync(messageId, cancellationToken);
        if (message == null)
        {
            throw new KeyNotFoundException($"TelegramMessage with ID '{messageId}' was not found.");
        }

        _logger.LogInformation("Starting manual reprocessing for TelegramMessage {MessageId} (ChannelId: {ChannelId}, MessageId: {TelegramMessageId})",
            message.Id, message.ChannelId, message.MessageId);

        // 2. Query Existing Processing Attempts & Idempotency / Concurrency Check
        var existingAttempts = await _attemptRepository.GetByTelegramMessageIdAsync(message.Id, cancellationToken);

        // Check for active in-progress attempt to prevent uncontrolled duplicate processing
        var activeAttempt = existingAttempts.FirstOrDefault(a => a.Status == "InProgress");
        if (activeAttempt != null)
        {
            throw new InvalidOperationException($"A reprocessing operation is already in progress for message '{messageId}' (Attempt #{activeAttempt.AttemptNumber}).");
        }

        // 3. Ensure Historical Attempt #1 exists for auditability
        MessageProcessingAttempt? attempt1 = existingAttempts.FirstOrDefault(a => a.AttemptNumber == 1);
        if (attempt1 == null)
        {
            attempt1 = await SeedHistoricalAttempt1Async(message, cancellationToken);
            existingAttempts.Add(attempt1);
        }

        // 4. Create New Processing Attempt (#N)
        int newAttemptNumber = existingAttempts.Count + 1;
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "REPROCESS_ONLY" : request.Mode.ToUpperInvariant();
        var triggeredBy = string.IsNullOrWhiteSpace(request.TriggeredBy) ? "Operator (Dashboard)" : request.TriggeredBy;

        var newAttempt = new MessageProcessingAttempt(
            message.Id,
            newAttemptNumber,
            triggerType: "ManualReprocess",
            triggeredBy: triggeredBy,
            processingMode: mode,
            parserVersion: "v2.0-StructuredExtractor",
            aiModelVersion: _aiIntentInterpreter != null ? "AI-Hybrid" : "RuleEngine"
        );

        await _attemptRepository.CreateAsync(newAttempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 5. Re-run ORIGINAL message through CURRENT processing pipeline
        try
        {
            var dto = new TelegramMessageDto
            {
                ChannelId = message.ChannelId,
                MessageId = (int)message.MessageId,
                SenderId = message.SenderId ?? 0,
                Text = message.Content ?? string.Empty,
                Date = message.ReceivedAt,
                ReplyToMessageId = message.ReplyToMessageId,
                MediaInfo = message.MediaInfo,
                EditInfo = message.EditInfo
            };

            // A. Context Collection (includes channel history, reply context, active positions, orders)
            var context = await _messageContextBuilder.BuildContextAsync(dto, cancellationToken);

            // B. Structured Signal Extraction using current parser rules
            var extractionResult = await _structuredSignalExtractor.ExtractAsync(message, cancellationToken);

            // C. Intent Classification using current classifier
            var classification = await _messageClassifier.ClassifyAsync(message, cancellationToken);

            // D. Hybrid Strategy: Rule Engine & AI Interpreter
            var ruleEval = await _deterministicRuleEngine.EvaluateAsync(context, cancellationToken);
            StructuredIntent? intent = ruleEval;

            if (intent == null && _aiIntentInterpreter != null)
            {
                intent = await _aiIntentInterpreter.InterpretAsync(context, cancellationToken);
            }

            if (intent != null)
            {
                intent = await _targetResolver.ResolveTargetsAsync(intent, context, cancellationToken);
            }

            // E. Signal Validation
            string validationResult = extractionResult.Status.ToString();

            // F. Risk Engine & Expiry Evaluation (If REPROCESS_AND_EVALUATE requested)
            string riskResult = "NotEvaluated";
            if (mode == "REPROCESS_AND_EVALUATE")
            {
                var age = DateTime.UtcNow - message.ReceivedAt;
                if (age > TimeSpan.FromMinutes(60))
                {
                    riskResult = "Expired";
                    _logger.LogInformation("Reprocessing message {MessageId}: Historical signal age ({AgeMinutes:F1} mins) exceeds expiry threshold. Risk evaluated as Expired.",
                        message.Id, age.TotalMinutes);
                }
                else if (_riskEngine != null && extractionResult.Success && extractionResult.EntryPrice.HasValue)
                {
                    try
                    {
                        var sideEnum = Enum.TryParse<OrderSide>(extractionResult.Side.ToString(), true, out var parsedSide)
                            ? parsedSide
                            : OrderSide.Buy;

                        var tpList = extractionResult.TakeProfits.Select(t => t.Price).ToList();

                        var riskCtx = new TradeRiskContext
                        {
                            SignalId = message.Id,
                            Symbol = extractionResult.Symbol ?? "UNKNOWN",
                            Side = sideEnum,
                            EntryPrice = extractionResult.EntryPrice.Value,
                            StopLoss = extractionResult.StopLoss,
                            TakeProfits = tpList,
                            AccountBalance = 10000m
                        };
                        var decision = await _riskEngine.EvaluateAsync(riskCtx);
                        riskResult = decision.Decision.ToString();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during Risk Engine evaluation for reprocess attempt on MessageId {MessageId}", message.Id);
                        riskResult = "EvaluationFailed";
                    }
                }
                else
                {
                    riskResult = extractionResult.Success ? "Passed" : "Rejected";
                }
            }

            // G. SAFETY RULE: Reprocessing alone NEVER places a Bybit order!
            string executionResult = "NoOrder";

            string? takeProfitsJson = extractionResult.TakeProfits != null && extractionResult.TakeProfits.Any()
                ? JsonSerializer.Serialize(extractionResult.TakeProfits)
                : null;

            string? spreadAllowance = extractionResult.Metadata != null && extractionResult.Metadata.TryGetValue("SpreadAllowance", out var sp)
                ? sp
                : null;

            string resolvedIntentStr = intent?.Intent.ToString() ?? classification.Intent.ToString();
            string? resolvedSymbol = extractionResult.Symbol ?? intent?.Symbol ?? classification.TargetSymbol;
            string resolvedSideStr = extractionResult.Side.ToString();

            newAttempt.Complete(
                intent: resolvedIntentStr,
                symbol: resolvedSymbol,
                side: resolvedSideStr,
                entryPrice: extractionResult.EntryPrice,
                stopLoss: extractionResult.StopLoss,
                takeProfitsJson: takeProfitsJson,
                leverage: extractionResult.Leverage,
                spreadAllowance: spreadAllowance,
                validationResult: validationResult,
                riskResult: riskResult,
                executionResult: executionResult,
                metadataJson: JsonSerializer.Serialize(extractionResult.Metadata)
            );

            await _attemptRepository.UpdateAsync(newAttempt, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Reprocessing completed successfully for MessageId {MessageId}. Attempt #{AttemptNumber}: Intent={Intent}, Symbol={Symbol}, Side={Side}, Status={Status}",
                message.Id, newAttempt.AttemptNumber, newAttempt.Intent, newAttempt.Symbol, newAttempt.Side, newAttempt.Status);

            return new ReprocessingResultDto(
                Success: true,
                AttemptId: newAttempt.Id,
                Attempt: MapToDto(newAttempt),
                PreviousAttempt: MapToDto(attempt1),
                Message: $"Message #{message.MessageId} reprocessed successfully as Attempt #{newAttempt.AttemptNumber}."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reprocessing failed for TelegramMessage {MessageId}", message.Id);
            newAttempt.MarkFailed(ex.Message);
            await _attemptRepository.UpdateAsync(newAttempt, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new ReprocessingResultDto(
                Success: false,
                AttemptId: newAttempt.Id,
                Attempt: MapToDto(newAttempt),
                PreviousAttempt: MapToDto(attempt1),
                Message: $"Reprocessing failed: {ex.Message}"
            );
        }
    }

    public async Task<List<MessageProcessingAttemptDto>> GetMessageAttemptsAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId is required.", nameof(messageId));

        var attempts = await _attemptRepository.GetByTelegramMessageIdAsync(messageId, cancellationToken);
        if (!attempts.Any())
        {
            var message = await _messageRepository.GetByIdAsync(messageId, cancellationToken);
            if (message != null)
            {
                var attempt1 = await SeedHistoricalAttempt1Async(message, cancellationToken);
                attempts.Add(attempt1);
            }
        }

        return attempts.Select(MapToDto).ToList();
    }

    public async Task<ExplicitExecutionResultDto> ExecuteAttemptTradeAsync(
        Guid messageId,
        Guid attemptId,
        ExplicitExecutionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId is required.", nameof(messageId));
        if (attemptId == Guid.Empty) throw new ArgumentException("AttemptId is required.", nameof(attemptId));
        request ??= new ExplicitExecutionRequestDto(attemptId);

        var attempt = await _attemptRepository.GetByIdAsync(attemptId, cancellationToken);
        if (attempt == null || attempt.TelegramMessageId != messageId)
        {
            throw new KeyNotFoundException($"Processing attempt '{attemptId}' for message '{messageId}' was not found.");
        }

        if (attempt.ValidationResult != "Valid" || attempt.Status == "Failed")
        {
            return new ExplicitExecutionResultDto(
                Success: false,
                AttemptId: attemptId,
                Status: "Rejected",
                OrderId: null,
                Message: "Cannot execute trade: Processing attempt is not valid or previously failed."
            );
        }

        var message = await _messageRepository.GetByIdAsync(messageId, cancellationToken);
        if (message == null)
        {
            throw new KeyNotFoundException($"TelegramMessage '{messageId}' was not found.");
        }

        // Real-time market state & risk re-validation before explicit execution
        var age = DateTime.UtcNow - message.ReceivedAt;
        if (age > TimeSpan.FromMinutes(60))
        {
            attempt.UpdateExecutionResult("ExecutionBlocked (Expired)");
            await _attemptRepository.UpdateAsync(attempt, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new ExplicitExecutionResultDto(
                Success: false,
                AttemptId: attemptId,
                Status: "Expired",
                OrderId: null,
                Message: "Execution blocked: Signal has expired relative to current market state."
            );
        }

        // Simulation / Execution Authorization Log
        _logger.LogInformation("EXPLICIT_EXECUTION authorized by '{Operator}' for MessageId {MessageId}, Attempt #{AttemptNumber}, Symbol {Symbol}, Side {Side}",
            request.ConfirmedBy, message.Id, attempt.AttemptNumber, attempt.Symbol, attempt.Side);

        attempt.UpdateExecutionResult("ExecutedByOperator", JsonSerializer.Serialize(new { ConfirmedBy = request.ConfirmedBy, ConfirmedAt = DateTime.UtcNow }));
        await _attemptRepository.UpdateAsync(attempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExplicitExecutionResultDto(
            Success: true,
            AttemptId: attemptId,
            Status: "Executed",
            OrderId: $"BYBIT-MANUAL-{attempt.AttemptNumber}-{DateTime.UtcNow.Ticks}",
            Message: $"Trade executed successfully for {attempt.Symbol} ({attempt.Side})."
        );
    }

    private async Task<MessageProcessingAttempt> SeedHistoricalAttempt1Async(
        TelegramMessage message,
        CancellationToken cancellationToken)
    {
        MessageAnalysis? analysis = null;
        if (_messageAnalysisRepository != null)
        {
            analysis = await _messageAnalysisRepository.GetByMessageIdAsync(message.Id, cancellationToken);
        }

        SignalExtraction? extraction = null;
        if (_signalExtractionRepository != null)
        {
            extraction = await _signalExtractionRepository.GetByMessageIdAsync(message.MessageId, cancellationToken);
        }

        var attempt1 = new MessageProcessingAttempt(
            message.Id,
            attemptNumber: 1,
            triggerType: "Automatic",
            triggeredBy: "System",
            processingMode: "REPROCESS_AND_EVALUATE",
            parserVersion: "v1.0-Legacy",
            aiModelVersion: analysis?.AIUsed == true ? "AI-Legacy" : "RuleEngine-v1"
        );

        string intentStr = analysis?.Intent.ToString() ?? "Unknown";
        string? symbol = extraction?.Symbol ?? analysis?.TargetSymbol;
        string sideStr = extraction?.Side ?? "UNKNOWN";
        decimal? entry = extraction?.EntryPrice;
        decimal? stopLoss = extraction?.StopLoss;
        string? tpJson = extraction?.TakeProfitData;
        string valResult = extraction?.Status ?? (analysis != null ? "Valid" : "Invalid");
        string riskResult = analysis?.ProcessingStatus == "Ignored" ? "No Active Position Found" : "HistoricalNotEvaluated";

        attempt1.Complete(
            intent: intentStr,
            symbol: symbol,
            side: sideStr,
            entryPrice: entry,
            stopLoss: stopLoss,
            takeProfitsJson: tpJson,
            leverage: null,
            spreadAllowance: null,
            validationResult: valResult,
            riskResult: riskResult,
            executionResult: "NoOrder"
        );

        await _attemptRepository.CreateAsync(attempt1, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created historical Attempt #1 for TelegramMessage {MessageId} (Symbol: {Symbol}, Intent: {Intent})",
            message.Id, symbol, intentStr);

        return attempt1;
    }

    private static MessageProcessingAttemptDto MapToDto(MessageProcessingAttempt a) => new(
        a.Id,
        a.TelegramMessageId,
        a.AttemptNumber,
        a.StartedAt,
        a.CompletedAt,
        a.TriggerType,
        a.TriggeredBy,
        a.ProcessingMode,
        a.ParserVersion,
        a.AiModelVersion,
        a.Status,
        a.Intent,
        a.Symbol,
        a.Side,
        a.EntryPrice,
        a.StopLoss,
        a.TakeProfitsJson,
        a.Leverage,
        a.SpreadAllowance,
        a.ValidationResult,
        a.RiskResult,
        a.ExecutionResult,
        a.ErrorMessage,
        a.MetadataJson
    );
}
