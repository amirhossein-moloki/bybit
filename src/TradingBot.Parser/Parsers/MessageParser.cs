using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
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
              signalParser)
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
        ISignalParser? signalParser = null)
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
    }

    public async Task<ParsedMessageResult> ParseAsync(TelegramMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        _logger.LogInformation("Parsing TelegramMessage message {MessageId} from channel {ChannelId}", message.Id, message.ChannelId);

        try
        {
            // 1. Processing Idempotency: Check if already processed
            var existingAnalysis = await _analysisRepository.GetByMessageIdAsync(message.Id, cancellationToken);
            if (existingAnalysis != null)
            {
                _logger.LogInformation("Message {MessageId} was already processed. Returning existing analysis.", message.Id);
                return ReconstructResultFromAnalysis(existingAnalysis);
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

            if (decision.ShouldUseAI && _aiAnalyzer != null)
            {
                // Run AI Analyzer Flow
                var aiResult = await _aiAnalyzer.AnalyzeMessageAsync(message, contextSummary, cancellationToken);

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
                // Use Rule-Based Result
                result = ruleBasedResult;
            }

            // 6. Context Resolution & State Machine Updates
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

            // 7. Persist analysis & message processed status
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
                aiUsed: decision.ShouldUseAI,
                DateTime.UtcNow
            );

            await _analysisRepository.CreateAsync(finalAnalysis, cancellationToken);
            message.MarkProcessed();
            await _messageRepository.MarkProcessedAsync(message.Id, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved MessageAnalysis for message {MessageId} with type {MessageType} and confidence {Confidence}",
                message.Id, result.Type, result.Confidence);

            // 8. Publish Intelligence Event
            IIntelligenceEvent? @event = result.Type switch
            {
                MessageType.SIGNAL => new SignalDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                MessageType.TRADE_UPDATE => new TradeUpdateDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                MessageType.CANCEL_COMMAND => new CancelCommandDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), result.Source.ToString(), extractedDataJson),
                _ => null
            };

            if (@event != null)
            {
                await _eventPublisher.PublishAsync(@event, cancellationToken);
                _logger.LogInformation("Published IntelligenceEvent {EventName} for message {MessageId}", @event.GetType().Name, message.Id);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parser Failed unexpectedly for TelegramMessage {MessageId}.", message.Id);
            return new ParsedMessageResult
            {
                Type = MessageType.UNKNOWN,
                Confidence = 0.0m,
                ErrorMessage = $"Unexpected parser failure: {ex.Message}",
                Source = ParserSource.RULE_BASED
            };
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
