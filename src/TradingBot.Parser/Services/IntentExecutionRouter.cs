using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Configuration;
using TradingBot.Telegram.Models;

namespace TradingBot.Parser.Services;

public class IntentExecutionRouter : IIntentExecutionRouter
{
    private readonly IMessageContextBuilder _contextBuilder;
    private readonly IDeterministicRuleEngine _ruleEngine;
    private readonly IAIIntentInterpreter _aiInterpreter;
    private readonly ITargetResolver _targetResolver;
    private readonly IIntentSafetyValidator _safetyValidator;
    private readonly IBreakEvenManager? _breakEvenManager;
    private readonly IPositionCloseManager? _positionCloseManager;
    private readonly ISignalStorageQueue? _signalQueue;
    private readonly MessageIntelligenceOptions _options;
    private readonly ILogger<IntentExecutionRouter> _logger;

    // Thread-safe in-memory idempotency deduplication tracker
    private static readonly ConcurrentDictionary<string, DateTime> ProcessedIntentsCache = new();

    public IntentExecutionRouter(
        IMessageContextBuilder contextBuilder,
        IDeterministicRuleEngine ruleEngine,
        IAIIntentInterpreter aiInterpreter,
        ITargetResolver targetResolver,
        IIntentSafetyValidator safetyValidator,
        IOptions<MessageIntelligenceOptions> options,
        ILogger<IntentExecutionRouter> logger,
        IBreakEvenManager? breakEvenManager = null,
        IPositionCloseManager? positionCloseManager = null,
        ISignalStorageQueue? signalQueue = null)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _aiInterpreter = aiInterpreter ?? throw new ArgumentNullException(nameof(aiInterpreter));
        _targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
        _safetyValidator = safetyValidator ?? throw new ArgumentNullException(nameof(safetyValidator));
        _options = options?.Value ?? new MessageIntelligenceOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _breakEvenManager = breakEvenManager;
        _positionCloseManager = positionCloseManager;
        _signalQueue = signalQueue;
    }

    public async Task ProcessMessageIntelligenceAsync(
        TelegramMessageDto message,
        CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var correlationId = $"tg-{message.ChannelId}-{message.MessageId}";

        // 1. Context Resolution
        var context = await _contextBuilder.BuildContextAsync(message, cancellationToken);

        // 2. Deterministic Fast Path
        var intent = await _ruleEngine.EvaluateAsync(context, cancellationToken);

        // 3. AI Fallback if rule engine returned null or low confidence
        if (intent == null)
        {
            _logger.LogInformation("IntentExecutionRouter: MessageId {MessageId} requires AI Fallback interpretation.", message.MessageId);
            intent = await _aiInterpreter.InterpretAsync(context, cancellationToken);
        }

        // 4. Target Resolution
        intent = await _targetResolver.ResolveTargetsAsync(intent, context, cancellationToken);

        // Structured Trace Logging
        _logger.LogInformation(
            "MessageIntelligenceTrace: CorrelationId={CorrelationId}, MessageId={MessageId}, ChannelId={ChannelId}, " +
            "ReplyToMessageId={ReplyToMessageId}, Method={DetectionMethod}, MessageType={MessageType}, Intent={Intent}, " +
            "Scope={Scope}, Targets=[{Targets}], Confidence={Confidence}, RequiresExecution={RequiresExecution}, Reason={Reason}",
            correlationId, message.MessageId, message.ChannelId, message.ReplyToMessageId,
            intent.DetectionMethod, intent.MessageType, intent.Intent, intent.Scope,
            string.Join(",", intent.Targets ?? new List<string>()), intent.Confidence,
            intent.RequiresExecution, intent.Reason);

        // 5. Handle New Signals
        if (intent.MessageType == IntelligenceMessageType.NEW_SIGNAL)
        {
            if (_signalQueue != null)
            {
                _logger.LogInformation("IntentExecutionRouter: Routing NEW_SIGNAL candidate to Existing Signal Detection Queue. ChannelId: {ChannelId}, MessageId: {MessageId}",
                    message.ChannelId, message.MessageId);

                var candidate = new SignalCandidate
                {
                    ChannelId = message.ChannelId,
                    MessageId = message.MessageId,
                    RawText = message.Text,
                    DetectedAt = message.Date,
                    DetectedSymbol = intent.Targets?.FirstOrDefault() ?? "UNKNOWN",
                    DetectedSide = "BUY",
                    DetectionScore = (int)Math.Round((double)intent.Confidence * 100)
                };
                await _signalQueue.EnqueueAsync(candidate);
            }
            return;
        }

        // If message is STATUS, COMMENTARY, QUESTION, NO_ACTION, UNKNOWN -> do not execute trading actions
        if (!intent.RequiresExecution || intent.MessageType != IntelligenceMessageType.COMMAND)
        {
            _logger.LogInformation("IntentExecutionRouter: MessageId {MessageId} (Type: {MessageType}, Intent: {Intent}) requires no trading execution.",
                message.MessageId, intent.MessageType, intent.Intent);
            return;
        }

        // 6. Idempotency Check
        var targetKeyStr = string.Join("-", intent.Targets ?? new List<string>());
        var idempotencyKey = $"intent-{message.ChannelId}-{message.MessageId}-{intent.Intent}-{targetKeyStr}";

        if (ProcessedIntentsCache.TryGetValue(idempotencyKey, out _))
        {
            _logger.LogWarning("IntentExecutionRouter: Idempotent duplicate intent detected and skipped. IdempotencyKey: {Key}", idempotencyKey);
            return;
        }

        // 7. Safety & Business Validation
        var (isSafe, safetyReason) = await _safetyValidator.ValidateSafetyAsync(intent, context, cancellationToken);
        if (!isSafe)
        {
            _logger.LogWarning("IntentExecutionRouter: Safety validation failed for MessageId {MessageId}. Reason: {Reason}",
                message.MessageId, safetyReason);
            return;
        }

        // 8. Shadow Mode Check
        if (_options.ShadowMode)
        {
            _logger.LogInformation("SHADOW_MODE ENABLED: Intent Execution Simulated [No Live Exchange Calls Executed]. CorrelationId={CorrelationId}, Intent={Intent}, Targets=[{Targets}]",
                correlationId, intent.Intent, string.Join(",", intent.Targets ?? new List<string>()));
            ProcessedIntentsCache.TryAdd(idempotencyKey, DateTime.UtcNow);
            return;
        }

        // 9. Execute Validated Command via Domain Services
        try
        {
            switch (intent.Intent)
            {
                case TradingIntent.RISK_FREE:
                case TradingIntent.RISK_FREE_ALL:
                    if (_breakEvenManager != null)
                    {
                        var breakEvenSettings = new BreakEvenSettings
                        {
                            Enabled = true,
                            TriggerType = BreakEvenTriggerType.Percentage,
                            TriggerValue = 0.001m,
                            Offset = 0.0m
                        };

                        foreach (var position in context.ActivePositions)
                        {
                            if (intent.Intent == TradingIntent.RISK_FREE_ALL || (intent.Targets != null && intent.Targets.Contains(position.Symbol, StringComparer.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("IntentExecutionRouter: Executing BreakEven command for PositionId {PositionId} ({Symbol})",
                                    position.PositionId, position.Symbol);
                                await _breakEvenManager.ExecuteBreakEvenCheckAsync(position.PositionId, position.EntryPrice, breakEvenSettings, cancellationToken);
                            }
                        }
                    }
                    break;

                case TradingIntent.CLOSE:
                case TradingIntent.CLOSE_ALL:
                    if (_positionCloseManager != null)
                    {
                        foreach (var position in context.ActivePositions)
                        {
                            if (intent.Intent == TradingIntent.CLOSE_ALL || (intent.Targets != null && intent.Targets.Contains(position.Symbol, StringComparer.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("IntentExecutionRouter: Executing ClosePosition command for PositionId {PositionId} ({Symbol})",
                                    position.PositionId, position.Symbol);
                                await _positionCloseManager.ClosePositionAsync(position.PositionId, CloseReason.Manual, null, "Telegram Command", cancellationToken);
                            }
                        }
                    }
                    break;
            }

            ProcessedIntentsCache.TryAdd(idempotencyKey, DateTime.UtcNow);
            _logger.LogInformation("IntentExecutionRouter: Successfully executed command for CorrelationId {CorrelationId}", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IntentExecutionRouter: Error executing trading command for CorrelationId {CorrelationId}", correlationId);
        }
    }
}
