using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Configuration;
using TradingBot.Application.SignalIntelligence.Validation;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Entities;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Events;
using TradingBot.Domain.SignalIntelligence.Interfaces;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Parsers;

public class MessageParser : IMessageParser
{
    private readonly IMessagePreprocessor _preprocessor;
    private readonly IMessageClassifier _classifier;
    private readonly IMessageAnalysisRepository _analysisRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly ISignalParser? _signalParser;
    private readonly IIntelligenceEventPublisher _eventPublisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MessageParser> _logger;

    // AI and Context dependencies
    private readonly IAIAnalyzer? _aiAnalyzer;
    private readonly IAIDecisionEngine? _decisionEngine;
    private readonly IConversationContextManager? _contextManager;
    private readonly ISignalContextRepository? _signalContextRepository;
    private readonly ISignalRepository? _signalRepository;

    // Reliability and Validation dependencies
    private readonly ISignalValidationService? _validationService;
    private readonly IMessageProcessingTrackerRepository? _trackerRepository;
    private readonly IFailedMessageAnalysisRepository? _failedRepository;
    private readonly IMetricsService? _metricsService;
    private readonly SignalIntelligenceOptions _siOptions;

    // Overloaded constructor for 100% backward compatibility
    public MessageParser(
        IMessagePreprocessor preprocessor,
        IMessageClassifier classifier,
        IMessageAnalysisRepository analysisRepository,
        IMessageRepository messageRepository,
        IIntelligenceEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        ILogger<MessageParser> logger,
        ISignalParser? signalParser = null)
        : this(
              preprocessor,
              classifier,
              analysisRepository,
              messageRepository,
              eventPublisher,
              unitOfWork,
              logger,
              null,
              null,
              null,
              null,
              null,
              signalParser,
              null,
              null,
              null,
              null,
              null)
    {
    }

    public MessageParser(
        IMessagePreprocessor preprocessor,
        IMessageClassifier classifier,
        IMessageAnalysisRepository analysisRepository,
        IMessageRepository messageRepository,
        IIntelligenceEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        ILogger<MessageParser> logger,
        IAIAnalyzer? aiAnalyzer,
        IAIDecisionEngine? decisionEngine,
        IConversationContextManager? contextManager,
        ISignalContextRepository? signalContextRepository,
        ISignalRepository? signalRepository,
        ISignalParser? signalParser = null,
        ISignalValidationService? validationService = null,
        IMessageProcessingTrackerRepository? trackerRepository = null,
        IFailedMessageAnalysisRepository? failedRepository = null,
        IMetricsService? metricsService = null,
        IOptions<SignalIntelligenceOptions>? siOptions = null)
    {
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _analysisRepository = analysisRepository ?? throw new ArgumentNullException(nameof(analysisRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiAnalyzer = aiAnalyzer;
        _decisionEngine = decisionEngine;
        _contextManager = contextManager;
        _signalContextRepository = signalContextRepository;
        _signalRepository = signalRepository;
        _signalParser = signalParser;

        _validationService = validationService;
        _trackerRepository = trackerRepository;
        _failedRepository = failedRepository;
        _metricsService = metricsService;
        _siOptions = siOptions?.Value ?? new SignalIntelligenceOptions();
    }

    public async Task<ParsedMessageResult> ParseAsync(TelegramMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("MessageReceived: Received message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
            message.MessageId, message.ChannelId, message.Id);

        _metricsService?.IncrementMessagesProcessed();

        // Ensure we have a tracking state initialized to RECEIVED
        MessageProcessingTracker? tracker = null;
        if (_trackerRepository != null)
        {
            tracker = await _trackerRepository.GetByTelegramMessageIdAsync(message.Id, cancellationToken);
            if (tracker == null)
            {
                tracker = new MessageProcessingTracker(message.Id, "RECEIVED");
                await _trackerRepository.CreateAsync(tracker, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        try
        {
            // Transition state to PROCESSING
            if (tracker != null)
            {
                tracker.TransitionTo("PROCESSING");
                _trackerRepository!.Update(tracker);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("AnalysisStarted: Started parsing message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
                message.MessageId, message.ChannelId, message.Id);

            // 1. Processing Idempotency & Duplicate Prevention: Check if already processed
            var existingAnalysis = await _analysisRepository.GetByMessageIdAsync(message.Id, cancellationToken);
            if (existingAnalysis != null)
            {
                _logger.LogInformation("Message {MessageId} was already processed. Returning existing analysis.", message.Id);
                stopwatch.Stop();
                _metricsService?.RecordAverageProcessingTime(stopwatch.Elapsed.TotalMilliseconds);
                return ReconstructResultFromAnalysis(existingAnalysis);
            }

            // Check duplicates by ChannelId + MessageId
            var existingMessage = await _messageRepository.GetByChannelMessageIdAsync(message.ChannelId, message.MessageId, cancellationToken);
            if (existingMessage != null && existingMessage.Id != message.Id)
            {
                _logger.LogWarning("DuplicateDetected: Duplicate message detected for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
                    message.MessageId, message.ChannelId, message.Id);

                _metricsService?.IncrementDuplicateCount();

                var existingAnalysisForMsg = await _analysisRepository.GetByMessageIdAsync(existingMessage.Id, cancellationToken);
                if (existingAnalysisForMsg != null)
                {
                    stopwatch.Stop();
                    _metricsService?.RecordAverageProcessingTime(stopwatch.Elapsed.TotalMilliseconds);
                    return ReconstructResultFromAnalysis(existingAnalysisForMsg);
                }
            }

            // 2. Preprocess raw content
            var preprocessed = _preprocessor.Preprocess(message.Content);

            // 3. Build Conversation Context Summary (if context manager is configured)
            string contextSummary = string.Empty;
            if (_contextManager != null)
            {
                var context = await _contextManager.GetContextAsync(message.ChannelId, cancellationToken);
                contextSummary = _contextManager.GetContextSummary(context);
            }

            // 4. Provisional/Rule-Based Classification and Extraction
            _logger.LogInformation("ParserUsed: Rule-based parser used for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
                message.MessageId, message.ChannelId, message.Id);

            var ruleBasedResult = await ExecuteRuleBasedExtractionAsync(message, preprocessed, cancellationToken);

            // 5. Determine if AI is required
            var decision = (_decisionEngine != null)
                ? _decisionEngine.DetermineAIUsage(message, ruleBasedResult)
                : new AIProcessingDecision { ShouldUseAI = false, Reason = "AI Engine is not configured in this context." };

            _logger.LogInformation("AI decision for message {MessageId}: ShouldUseAI={ShouldUseAI}, Reason={Reason}",
                message.Id, decision.ShouldUseAI, decision.Reason);

            ParsedMessageResult result;
            string? aiActionStr = null;
            string? aiReason = null;
            bool usedAI = false;

            // Define the primary operation to run (AI Analyzer if required, or Rule-Based)
            if (decision.ShouldUseAI && _aiAnalyzer != null)
            {
                usedAI = true;
                _logger.LogInformation("AIUsed: AI analyzer used for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
                    message.MessageId, message.ChannelId, message.Id);

                _metricsService?.IncrementAIUsageCount();

                // Run AI with retry logic
                var aiResult = await ExecuteWithRetryAsync(async () =>
                {
                    try
                    {
                        var analysisResult = await _aiAnalyzer.AnalyzeMessageAsync(message, contextSummary, cancellationToken);

                        // Schema validation of AI output string (if we can serialize / validate its output)
                        if (_validationService != null)
                        {
                            var aiPayloadString = JsonSerializer.Serialize(analysisResult);
                            var aiSchemaValidation = _validationService.ValidateAIResponse(aiPayloadString);
                            if (!aiSchemaValidation.IsValid)
                            {
                                throw new Exception($"AI Response Schema Validation Failed: {string.Join("; ", aiSchemaValidation.Errors)}");
                            }
                        }

                        return analysisResult;
                    }
                    catch (Exception ex)
                    {
                        _metricsService?.IncrementAIFailureCount();
                        throw;
                    }
                }, cancellationToken);

                result = new ParsedMessageResult
                {
                    Type = Enum.TryParse<MessageType>(aiResult.Type, out var parsedType) ? parsedType : MessageType.UNKNOWN,
                    Symbol = !string.IsNullOrEmpty(aiResult.Symbol) ? aiResult.Symbol.ToUpperInvariant() : null,
                    Side = Enum.TryParse<OrderSide>(aiResult.Side, true, out var parsedSide) ? parsedSide : null,
                    Entry = aiResult.Entry,
                    StopLoss = aiResult.StopLoss,
                    TakeProfits = aiResult.TakeProfits ?? new List<decimal>(),
                    Action = Enum.TryParse<TradeAction>(aiResult.Action, true, out var parsedAction) ? parsedAction : null,
                    Confidence = aiResult.Confidence,
                    Source = ParserSource.AI,
                    ErrorMessage = aiResult.Type == "UNKNOWN" ? aiResult.Reason : null
                };

                aiActionStr = aiResult.Action;
                aiReason = aiResult.Reason;

                if (result.Confidence < 0.70m)
                {
                    var lowPayload = JsonSerializer.Serialize(new { MessageId = message.Id, Confidence = result.Confidence, Reason = aiResult.Reason });
                    var lowEvent = new LowConfidenceDetected(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "AI_ANALYZER", lowPayload);
                    await _eventPublisher.PublishAsync(lowEvent, cancellationToken);
                }
            }
            else
            {
                // Rule-based Success Count
                _metricsService?.IncrementParserSuccessCount();
                result = ruleBasedResult;
            }

            // 6. Validation Layer: Run independent validation service
            if (_validationService != null)
            {
                var validationResult = _validationService.Validate(result);
                if (!validationResult.IsValid)
                {
                    _metricsService?.IncrementValidationFailureCount();
                    _logger.LogWarning("ValidationFailed: Validation failed for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId} because: {Reason}",
                        message.MessageId, message.ChannelId, message.Id, string.Join("; ", validationResult.Errors));

                    // If primary run fails validation, we check if we should fallback to AI (if we haven't already used it)
                    if (!usedAI && _aiAnalyzer != null)
                    {
                        _logger.LogWarning("Parser Failed validation. Falling back to AI Analyzer.");
                        usedAI = true;
                        _metricsService?.IncrementAIUsageCount();

                        var aiResult = await ExecuteWithRetryAsync(async () =>
                        {
                            try
                            {
                                var analysisResult = await _aiAnalyzer.AnalyzeMessageAsync(message, contextSummary, cancellationToken);
                                if (_validationService != null)
                                {
                                    var aiPayloadString = JsonSerializer.Serialize(analysisResult);
                                    var aiSchemaValidation = _validationService.ValidateAIResponse(aiPayloadString);
                                    if (!aiSchemaValidation.IsValid)
                                    {
                                        throw new Exception($"AI Response Schema Validation Failed: {string.Join("; ", aiSchemaValidation.Errors)}");
                                    }
                                }
                                return analysisResult;
                            }
                            catch (Exception)
                            {
                                _metricsService?.IncrementAIFailureCount();
                                throw;
                            }
                        }, cancellationToken);

                        result = new ParsedMessageResult
                        {
                            Type = Enum.TryParse<MessageType>(aiResult.Type, out var parsedType) ? parsedType : MessageType.UNKNOWN,
                            Symbol = !string.IsNullOrEmpty(aiResult.Symbol) ? aiResult.Symbol.ToUpperInvariant() : null,
                            Side = Enum.TryParse<OrderSide>(aiResult.Side, true, out var parsedSide) ? parsedSide : null,
                            Entry = aiResult.Entry,
                            StopLoss = aiResult.StopLoss,
                            TakeProfits = aiResult.TakeProfits ?? new List<decimal>(),
                            Action = Enum.TryParse<TradeAction>(aiResult.Action, true, out var parsedAction) ? parsedAction : null,
                            Confidence = aiResult.Confidence,
                            Source = ParserSource.AI,
                            ErrorMessage = aiResult.Type == "UNKNOWN" ? aiResult.Reason : null
                        };

                        aiActionStr = aiResult.Action;
                        aiReason = aiResult.Reason;

                        // Re-validate the fallback result
                        validationResult = _validationService.Validate(result);
                    }

                    if (!validationResult.IsValid)
                    {
                        throw new Exception($"Validation failed: {string.Join("; ", validationResult.Errors)}");
                    }
                }

                _logger.LogInformation("ValidationPassed: Validation passed for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
                    message.MessageId, message.ChannelId, message.Id);
            }

            // Transition state to ANALYZED, then VALIDATED
            if (tracker != null)
            {
                tracker.TransitionTo("ANALYZED");
                _trackerRepository!.Update(tracker);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                tracker.TransitionTo("VALIDATED");
                _trackerRepository!.Update(tracker);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // 7. Context Resolution & State Machine Updates
            if (result.Type == MessageType.SIGNAL && result.Confidence >= 0.70m && !string.IsNullOrEmpty(result.Symbol))
            {
                if (_signalRepository != null && _signalContextRepository != null)
                {
                    // Handle new SIGNAL context creation
                    var newSignal = new Signal(
                        message.ChannelId,
                        message.MessageId,
                        message.Content,
                        result.Symbol,
                        result.Side ?? OrderSide.Buy,
                        message.ReceivedAt
                    );

                    newSignal.UpdateParsedDetails(
                        result.Symbol,
                        result.Side ?? OrderSide.Buy,
                        result.Entry ?? 1.0m,
                        result.StopLoss,
                        result.TakeProfits.Any() ? result.TakeProfits.First() : null,
                        null
                    );

                    await _signalRepository.SaveAsync(newSignal, cancellationToken);

                    var signalContext = new SignalContext(
                        newSignal.Id,
                        message.ChannelId,
                        result.Symbol,
                        SignalState.RECEIVED,
                        null,
                        message.MessageId
                    );
                    await _signalContextRepository.CreateAsync(signalContext, cancellationToken);

                    // Transition state from RECEIVED to VALIDATED
                    var oldState = signalContext.CurrentState;
                    var nextState = SignalState.VALIDATED;
                    signalContext.UpdateState(nextState, "VALIDATED", message.MessageId);

                    // Publish ContextResolved Event
                    var resolvedPayload = JsonSerializer.Serialize(new { SignalId = newSignal.Id, Symbol = result.Symbol });
                    var resolvedEvent = new ContextResolved(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "MESSAGE_PARSER", resolvedPayload);
                    await _eventPublisher.PublishAsync(resolvedEvent, cancellationToken);

                    // Publish SignalStateChanged Event
                    var stateChangedPayload = JsonSerializer.Serialize(new { SignalId = newSignal.Id, Symbol = result.Symbol, OldState = oldState.ToString(), NewState = nextState.ToString() });
                    var stateChangedEvent = new SignalStateChanged(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "MESSAGE_PARSER", stateChangedPayload);
                    await _eventPublisher.PublishAsync(stateChangedEvent, cancellationToken);
                }
            }
            else if ((result.Type == MessageType.TRADE_UPDATE || result.Type == MessageType.CANCEL_COMMAND) && result.Confidence >= 0.70m)
            {
                if (_signalContextRepository != null)
                {
                    // Handle follow-up updates: resolve existing context
                    SignalContext? signalContext = null;
                    if (!string.IsNullOrEmpty(result.Symbol))
                    {
                        signalContext = await _signalContextRepository.GetActiveContextAsync(message.ChannelId, result.Symbol, cancellationToken);
                    }

                    if (signalContext == null)
                    {
                        // Fallback to latest active context in channel
                        signalContext = await _signalContextRepository.GetLatestActiveContextAsync(message.ChannelId, cancellationToken);
                    }

                    if (signalContext != null)
                    {
                        var oldState = signalContext.CurrentState;
                        var actionStr = result.Action?.ToString() ?? aiActionStr ?? string.Empty;
                        var nextState = MapActionStringToState(actionStr, oldState);

                        signalContext.UpdateState(nextState, actionStr, message.MessageId);
                        _signalContextRepository.Update(signalContext);

                        // Publish ContextResolved Event
                        var resolvedPayload = JsonSerializer.Serialize(new { SignalId = signalContext.SignalId, Symbol = signalContext.Symbol });
                        var resolvedEvent = new ContextResolved(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "MESSAGE_PARSER", resolvedPayload);
                        await _eventPublisher.PublishAsync(resolvedEvent, cancellationToken);

                        // Publish SignalStateChanged Event
                        var stateChangedPayload = JsonSerializer.Serialize(new { SignalId = signalContext.SignalId, Symbol = signalContext.Symbol, OldState = oldState.ToString(), NewState = nextState.ToString() });
                        var stateChangedEvent = new SignalStateChanged(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "MESSAGE_PARSER", stateChangedPayload);
                        await _eventPublisher.PublishAsync(stateChangedEvent, cancellationToken);
                    }
                }
            }

            // 8. Persist analysis & message processed status
            var extractedDataDict = new Dictionary<string, object>();
            extractedDataDict["type"] = result.Type.ToString();
            extractedDataDict["confidence"] = result.Confidence;
            extractedDataDict["source"] = result.Source.ToString();
            if (result.Symbol != null) extractedDataDict["symbol"] = result.Symbol;
            if (result.Side != null) extractedDataDict["side"] = result.Side.ToString();
            if (result.Entry != null) extractedDataDict["entry"] = result.Entry.Value;
            if (result.StopLoss != null) extractedDataDict["stop_loss"] = result.StopLoss.Value;
            if (result.TakeProfits.Any()) extractedDataDict["take_profit"] = result.TakeProfits;
            if (result.Action != null) extractedDataDict["action"] = result.Action.Value.ToString();
            else if (!string.IsNullOrEmpty(aiActionStr)) extractedDataDict["action"] = aiActionStr;
            if (!string.IsNullOrEmpty(aiReason)) extractedDataDict["reason"] = aiReason;

            string extractedDataJson = JsonSerializer.Serialize(extractedDataDict, new JsonSerializerOptions { WriteIndented = false });

            var finalAnalysis = new MessageAnalysis(
                message.Id,
                result.Type,
                result.Confidence,
                extractedDataJson,
                aiUsed: usedAI,
                DateTime.UtcNow
            );

            await _analysisRepository.CreateAsync(finalAnalysis, cancellationToken);
            message.MarkProcessed();
            await _messageRepository.MarkProcessedAsync(message.Id, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved MessageAnalysis for message {MessageId} with type {MessageType} and confidence {Confidence}",
                message.Id, result.Type, result.Confidence);

            // 9. Publish Reliable Intelligence Event
            IIntelligenceEvent? legacyEvent = result.Type switch
            {
                MessageType.SIGNAL => new SignalDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                MessageType.TRADE_UPDATE => new TradeUpdateDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                MessageType.CANCEL_COMMAND => new CancelCommandDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                _ => null
            };

            IIntelligenceEvent? @event = result.Type switch
            {
                MessageType.SIGNAL => new SignalIntelligenceCreated(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                MessageType.TRADE_UPDATE => new TradeUpdateDetected(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                MessageType.CANCEL_COMMAND => new TradeUpdateDetected(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                _ => null
            };

            if (legacyEvent != null)
            {
                await PublishEventWithRetryAsync(legacyEvent, cancellationToken);
            }

            if (@event != null)
            {
                await PublishEventWithRetryAsync(@event, cancellationToken);
                _logger.LogInformation("EventPublished: Event published for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId}",
                    message.MessageId, message.ChannelId, message.Id);
            }

            // Transition state to PUBLISHED
            if (tracker != null)
            {
                tracker.TransitionTo("PUBLISHED");
                _trackerRepository!.Update(tracker);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            stopwatch.Stop();
            _metricsService?.RecordAverageProcessingTime(stopwatch.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessingFailed: Message processing failed for message {MessageId} from Channel {ChannelId} with CorrelationId {CorrelationId} because: {Reason}",
                message.MessageId, message.ChannelId, message.Id, ex.Message);

            // Create FailedMessageAnalysis record
            if (_failedRepository != null)
            {
                try
                {
                    var failedAnalysis = new FailedMessageAnalysis(
                        message.Id,
                        ex.Message,
                        "MessageParser",
                        0,
                        "Failed"
                    );
                    await _failedRepository.CreateAsync(failedAnalysis, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Failed to persist FailedMessageAnalysis for message {MessageId}", message.Id);
                }
            }

            // Transition state to FAILED
            if (tracker != null)
            {
                try
                {
                    tracker.TransitionTo("FAILED");
                    _trackerRepository!.Update(tracker);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception stateEx)
                {
                    _logger.LogError(stateEx, "Failed to transition tracker to FAILED for message {MessageId}", message.Id);
                }
            }

            stopwatch.Stop();
            _metricsService?.RecordAverageProcessingTime(stopwatch.Elapsed.TotalMilliseconds);

            return new ParsedMessageResult
            {
                Type = MessageType.UNKNOWN,
                Confidence = 0.0m,
                ErrorMessage = $"Unexpected parser failure: {ex.Message}",
                Source = ParserSource.RULE_BASED
            };
        }
    }

    private async Task<AIUnderstandingResult> ExecuteWithRetryAsync(
        Func<Task<AIUnderstandingResult>> operation,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        int delay = _siOptions.RetryDelay; // in ms

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsTemporaryFailure(ex))
            {
                attempt++;
                if (attempt > _siOptions.MaxRetries)
                {
                    _logger.LogError(ex, "AI processing temporary failure retries exhausted. Attempt: {Attempt}", attempt);
                    throw;
                }

                _logger.LogWarning(ex, "AI processing temporary failure. Retrying attempt {Attempt} after delay {Delay} ms. Strategy: {Strategy}",
                    attempt, delay, _siOptions.BackoffStrategy);

                await Task.Delay(delay, cancellationToken);

                // Apply backoff strategy
                if (_siOptions.BackoffStrategy.Equals("Exponential", StringComparison.OrdinalIgnoreCase))
                {
                    delay *= 2;
                }
                else if (_siOptions.BackoffStrategy.Equals("Linear", StringComparison.OrdinalIgnoreCase))
                {
                    delay += _siOptions.RetryDelay;
                }
            }
            catch (Exception ex)
            {
                // Non-temporary failures should not be retried
                _logger.LogError(ex, "AI processing encountered non-retryable failure.");
                throw;
            }
        }
    }

    private bool IsTemporaryFailure(Exception ex)
    {
        // Retry: Timeout, Network Error, Temporary Provider Error
        // Do not retry: Invalid JSON, Invalid Message, Validation Failed (e.g. JsonException or custom validation exception)
        if (ex is JsonException) return false;
        if (ex is ArgumentException) return false;

        var msg = ex.Message.ToUpperInvariant();
        if (msg.Contains("TIMEOUT") || msg.Contains("NETWORK") || msg.Contains("UNAVAILABLE") || msg.Contains("503") || msg.Contains("502") || msg.Contains("504") || msg.Contains("429"))
        {
            return true;
        }

        if (ex is TaskCanceledException || ex is System.Net.Http.HttpRequestException || ex is TimeoutException)
        {
            return true;
        }

        return false;
    }

    private async Task PublishEventWithRetryAsync(IIntelligenceEvent @event, CancellationToken cancellationToken)
    {
        int attempt = 0;
        int maxAttempts = 3;
        int delay = 500;

        while (true)
        {
            try
            {
                await _eventPublisher.PublishAsync(@event, cancellationToken);
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxAttempts)
                {
                    _logger.LogError(ex, "Failed to publish intelligence event {EventName} after {MaxAttempts} attempts. Storing as failed event publishing.", @event.GetType().Name, maxAttempts);
                    if (_failedRepository != null)
                    {
                        var failedPublish = new FailedMessageAnalysis(
                            Guid.Parse(@event.CorrelationId),
                            $"Event publishing failed: {ex.Message}",
                            "IntelligenceEventPublisher",
                            attempt,
                            "Failed"
                        );
                        await _failedRepository.CreateAsync(failedPublish, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                    break;
                }

                _logger.LogWarning(ex, "Failed to publish event {EventName}. Retrying attempt {Attempt}...", @event.GetType().Name, attempt);
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
        }
    }

    private async Task<ParsedMessageResult> ExecuteRuleBasedExtractionAsync(TelegramMessage message, string preprocessed, CancellationToken cancellationToken)
    {
        var tempAnalysis = await _classifier.ClassifyAsync(message, cancellationToken);
        var detectedType = tempAnalysis.MessageType;

        var result = new ParsedMessageResult
        {
            Type = detectedType,
            Source = ParserSource.RULE_BASED
        };

        var detectedFields = new List<string>();

        if (detectedType == MessageType.SIGNAL)
        {
            if (_signalParser != null)
            {
                var parserContext = new ParserContext(message.Id, preprocessed, message.ChannelId.ToString(), message.ReceivedAt, "1.0");
                var parserResult = await _signalParser.ParseAsync(parserContext);

                if (parserResult.Success && parserResult.ParsedSignal != null)
                {
                    var parsedSignal = parserResult.ParsedSignal;
                    result.Symbol = parsedSignal.Symbol;
                    result.Side = parsedSignal.Side;
                    result.Entry = parsedSignal.EntryPrice;
                    result.StopLoss = parsedSignal.StopLoss;
                    result.TakeProfits = parsedSignal.TakeProfits ?? new List<decimal>();

                    if (result.Symbol != null) detectedFields.Add("Symbol");
                    if (result.Side != null) detectedFields.Add("Side");
                    if (result.Entry != null) detectedFields.Add("Entry");
                    if (result.StopLoss != null) detectedFields.Add("StopLoss");
                    if (result.TakeProfits.Any()) detectedFields.Add("TakeProfits");

                    var rangeMatch = Regex.Match(preprocessed, @"\b(?:ENTRY|ZONE|ورود|BUY\s+ZONE)\s*[:\s-]*([0-9.]+)\s*-\s*([0-9.]+)", RegexOptions.IgnoreCase);
                    if (rangeMatch.Success)
                    {
                        if (decimal.TryParse(rangeMatch.Groups[1].Value, out var min) && decimal.TryParse(rangeMatch.Groups[2].Value, out var max))
                        {
                            result.EntryRangeMin = min;
                            result.EntryRangeMax = max;
                            detectedFields.Add("EntryRange");
                        }
                    }
                }
                else
                {
                    result.ErrorMessage = parserResult.Errors != null ? string.Join("; ", parserResult.Errors) : "Signal extraction failed";
                }
            }
            else
            {
                result.Symbol = ExtractSymbolFallback(preprocessed);
                result.Side = ExtractSideFallback(preprocessed);
                result.Entry = ExtractEntryFallback(preprocessed);
                result.StopLoss = ExtractStopLossFallback(preprocessed);
                result.TakeProfits = ExtractTakeProfitsFallback(preprocessed);

                if (result.Symbol != null) detectedFields.Add("Symbol");
                if (result.Side != null) detectedFields.Add("Side");
                if (result.Entry != null) detectedFields.Add("Entry");
                if (result.StopLoss != null) detectedFields.Add("StopLoss");
                if (result.TakeProfits.Any()) detectedFields.Add("TakeProfits");
            }

            decimal calculatedConfidence = 0.5m;
            if (result.Symbol != null) calculatedConfidence += 0.15m;
            if (result.Side != null) calculatedConfidence += 0.15m;
            if (result.Entry != null || result.EntryRangeMin != null) calculatedConfidence += 0.05m;
            if (result.StopLoss != null) calculatedConfidence += 0.05m;
            if (result.TakeProfits.Any()) calculatedConfidence += 0.10m;

            result.Confidence = calculatedConfidence;
        }
        else if (detectedType == MessageType.TRADE_UPDATE)
        {
            result.Action = ExtractTradeAction(preprocessed);
            result.Confidence = 0.90m;
            detectedFields.Add("Action");

            var symbol = ExtractSymbolFallback(preprocessed);
            if (symbol != null)
            {
                result.Symbol = symbol;
                detectedFields.Add("Symbol");
            }
        }
        else if (detectedType == MessageType.CANCEL_COMMAND)
        {
            result.Action = TradeAction.CANCEL;
            result.Confidence = 0.95m;
            detectedFields.Add("Action");

            var symbol = ExtractSymbolFallback(preprocessed);
            if (symbol != null)
            {
                result.Symbol = symbol;
                detectedFields.Add("Symbol");
            }
        }
        else
        {
            result.Confidence = 0.0m;
        }

        result.DetectedFields = detectedFields;
        return result;
    }

    private ParsedMessageResult ReconstructResultFromAnalysis(MessageAnalysis analysis)
    {
        var result = new ParsedMessageResult
        {
            Type = analysis.MessageType,
            Confidence = analysis.Confidence,
            Source = analysis.AIUsed ? ParserSource.AI : ParserSource.RULE_BASED
        };

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(analysis.ExtractedData);
            if (dict != null)
            {
                if (dict.TryGetValue("symbol", out var sym)) result.Symbol = sym.GetString();
                if (dict.TryGetValue("side", out var sd) && Enum.TryParse<OrderSide>(sd.GetString(), out var side)) result.Side = side;
                if (dict.TryGetValue("entry", out var ent)) result.Entry = ent.GetDecimal();
                if (dict.TryGetValue("stop_loss", out var sl)) result.StopLoss = sl.GetDecimal();
                if (dict.TryGetValue("take_profit", out var tp))
                {
                    result.TakeProfits = tp.EnumerateArray().Select(x => x.GetDecimal()).ToList();
                }
                if (dict.TryGetValue("action", out var act) && Enum.TryParse<TradeAction>(act.GetString(), out var action)) result.Action = action;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconstruct ParsedMessageResult from existing analysis JSON.");
        }

        return result;
    }

    private SignalState MapActionStringToState(string actionStr, SignalState currentState)
    {
        if (string.IsNullOrWhiteSpace(actionStr)) return currentState;

        var upper = actionStr.ToUpperInvariant();
        return upper switch
        {
            "ACTIVATE_SIGNAL" or "ACTIVATE" or "TRIGGER" => SignalState.ACTIVE,
            "MOVE_STOP_TO_ENTRY" or "RISK_FREE" or "RISK-FREE" or "RISKFREE" => SignalState.RISK_FREE,
            "CLOSE_PARTIAL" or "PARTIAL_CLOSE" or "PARTIAL" or "SAVE_PROFIT" => SignalState.PARTIAL_CLOSE,
            "CLOSE_POSITION" or "CLOSE" or "EXIT" => SignalState.CLOSED,
            "CANCEL" or "CANCEL_ALL" or "CANCEL_ORDER" => SignalState.CANCELLED,
            "UPDATE_STOP_LOSS" or "UPDATE_SL" or "CHANGE_SL" => SignalState.MANAGED,
            "UPDATE_TAKE_PROFIT" or "UPDATE_TP" or "CHANGE_TP" => SignalState.MANAGED,
            _ => currentState
        };
    }

    private static readonly HashSet<string> ExcludedSymbolKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CANCEL", "CLOSE", "UPDATE", "BUY", "SELL", "STOP", "LIMIT", "ENTRY", "RISK", "FREE",
        "HALF", "NOW", "PARTIAL", "ALL", "ORDER", "ORDERS", "SL", "TP", "INFO", "LONG", "SHORT",
        "ZONE", "TARGET", "MARKET", "ورود", "حد سود", "حد ضرر", "سیو سود"
    };

    private string? ExtractSymbolFallback(string text)
    {
        var upper = text.ToUpperInvariant();
        var matches = Regex.Matches(upper, @"\b([A-Z0-9]{3,8}(?:/|-)?(?:USDT|USDC)?)\b");
        foreach (Match match in matches)
        {
            var value = match.Groups[1].Value;
            if (!ExcludedSymbolKeywords.Contains(value) && !value.All(char.IsDigit))
            {
                return value.Replace("/", "").Replace("-", "");
            }
        }
        return null;
    }

    private OrderSide? ExtractSideFallback(string text)
    {
        var upper = text.ToUpperInvariant();
        if (upper.Contains("BUY") || upper.Contains("LONG") || upper.Contains("خرید")) return OrderSide.Buy;
        if (upper.Contains("SELL") || upper.Contains("SHORT") || upper.Contains("فروش")) return OrderSide.Sell;
        return null;
    }

    private decimal? ExtractEntryFallback(string text)
    {
        var match = Regex.Match(text.ToUpperInvariant(), @"\b(?:ENTRY|ENT|ورود|@)[\s:]*([0-9.]+)");
        return match.Success && decimal.TryParse(match.Groups[1].Value, out var price) ? price : null;
    }

    private decimal? ExtractStopLossFallback(string text)
    {
        var match = Regex.Match(text.ToUpperInvariant(), @"\b(?:SL|STOP|حد ضرر)[\s:]*([0-9.]+)");
        return match.Success && decimal.TryParse(match.Groups[1].Value, out var sl) ? sl : null;
    }

    private IReadOnlyList<decimal> ExtractTakeProfitsFallback(string text)
    {
        var list = new List<decimal>();
        var matches = Regex.Matches(text.ToUpperInvariant(), @"\b(?:TP|TARGET|حد سود)[0-9]*[\s:]*([0-9.]+)");
        foreach (Match match in matches)
        {
            if (decimal.TryParse(match.Groups[1].Value, out var tp) && !list.Contains(tp))
            {
                list.Add(tp);
            }
        }
        return list;
    }

    private TradeAction? ExtractTradeAction(string text)
    {
        var upper = text.ToUpperInvariant();
        if (upper.Contains("RISK FREE") || upper.Contains("ریسک فری") || upper.Contains("فری کنید")) return TradeAction.MOVE_STOP_TO_ENTRY;
        if (upper.Contains("CLOSE PARTIAL") || upper.Contains("سیو سود") || upper.Contains("CLOSE HALF")) return TradeAction.CLOSE_PARTIAL;
        if (upper.Contains("CLOSE POSITION") || upper.Contains("EXIT NOW") || upper.Contains("ببندید") || upper.Contains("خروج")) return TradeAction.CLOSE_POSITION;
        if (upper.Contains("UPDATE SL") || upper.Contains("تغییر حد ضرر")) return TradeAction.UPDATE_STOP_LOSS;
        if (upper.Contains("UPDATE TP") || upper.Contains("تغییر حد سود")) return TradeAction.UPDATE_TAKE_PROFIT;
        return null;
    }
}
