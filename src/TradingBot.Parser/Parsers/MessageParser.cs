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

    public MessageParser(
        IMessagePreprocessor preprocessor,
        IMessageClassifier classifier,
        IMessageAnalysisRepository analysisRepository,
        IMessageRepository messageRepository,
        IIntelligenceEventPublisher eventPublisher,
        IUnitOfWork unitOfWork,
        ILogger<MessageParser> logger,
        ISignalParser? signalParser = null)
    {
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _analysisRepository = analysisRepository ?? throw new ArgumentNullException(nameof(analysisRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // 3. Classify Message
            var tempAnalysis = await _classifier.ClassifyAsync(message, cancellationToken);
            var detectedType = tempAnalysis.MessageType;

            var result = new ParsedMessageResult
            {
                Type = detectedType,
                Source = ParserSource.RULE_BASED
            };

            var extractedDataDict = new Dictionary<string, object>();
            extractedDataDict["type"] = detectedType.ToString();

            decimal calculatedConfidence = 0.0m;
            var detectedFields = new List<string>();

            // 4. Structured Extraction based on Classification
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

                        // Extract entry range if present
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
                    // Fallback basic signal parsing if ISignalParser is not registered (resilience)
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

                // Deterministic confidence calculation
                calculatedConfidence = 0.5m;
                if (result.Symbol != null) calculatedConfidence += 0.15m;
                if (result.Side != null) calculatedConfidence += 0.15m;
                if (result.Entry != null || result.EntryRangeMin != null) calculatedConfidence += 0.05m;
                if (result.StopLoss != null) calculatedConfidence += 0.05m;
                if (result.TakeProfits.Any()) calculatedConfidence += 0.10m;

                extractedDataDict["symbol"] = result.Symbol ?? string.Empty;
                extractedDataDict["side"] = result.Side?.ToString() ?? string.Empty;
                if (result.EntryRangeMin != null && result.EntryRangeMax != null)
                {
                    extractedDataDict["entry_range"] = new[] { result.EntryRangeMin, result.EntryRangeMax };
                }
                else
                {
                    extractedDataDict["entry"] = result.Entry ?? 0m;
                }
                extractedDataDict["stop_loss"] = result.StopLoss ?? 0m;
                extractedDataDict["take_profit"] = result.TakeProfits;
            }
            else if (detectedType == MessageType.TRADE_UPDATE)
            {
                result.Action = ExtractTradeAction(preprocessed);
                calculatedConfidence = 0.90m;
                detectedFields.Add("Action");

                var symbol = ExtractSymbolFallback(preprocessed);
                if (symbol != null)
                {
                    result.Symbol = symbol;
                    detectedFields.Add("Symbol");
                    extractedDataDict["symbol"] = symbol;
                }

                extractedDataDict["action"] = result.Action?.ToString() ?? "UNKNOWN";
            }
            else if (detectedType == MessageType.CANCEL_COMMAND)
            {
                result.Action = TradeAction.CANCEL;
                calculatedConfidence = 0.95m;
                detectedFields.Add("Action");

                var symbol = ExtractSymbolFallback(preprocessed);
                if (symbol != null)
                {
                    result.Symbol = symbol;
                    detectedFields.Add("Symbol");
                    extractedDataDict["symbol"] = symbol;
                }

                extractedDataDict["action"] = "CANCEL";
            }
            else
            {
                calculatedConfidence = 0.0m;
            }

            result.Confidence = calculatedConfidence;
            result.DetectedFields = detectedFields;

            extractedDataDict["confidence"] = calculatedConfidence;
            extractedDataDict["source"] = "RULE_BASED";

            string extractedDataJson = JsonSerializer.Serialize(extractedDataDict, new JsonSerializerOptions { WriteIndented = false });

            // 5. Persist Result
            var finalAnalysis = new MessageAnalysis(
                message.Id,
                detectedType,
                calculatedConfidence,
                extractedDataJson,
                aiUsed: false,
                DateTime.UtcNow
            );

            await _analysisRepository.CreateAsync(finalAnalysis, cancellationToken);
            message.MarkProcessed();
            await _messageRepository.MarkProcessedAsync(message.Id, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved MessageAnalysis for message {MessageId} with type {MessageType} and confidence {Confidence}",
                message.Id, detectedType, calculatedConfidence);

            // 6. Publish Intelligence Event
            IIntelligenceEvent? @event = detectedType switch
            {
                MessageType.SIGNAL => new SignalDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "RULE_BASED", extractedDataJson),
                MessageType.TRADE_UPDATE => new TradeUpdateDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "RULE_BASED", extractedDataJson),
                MessageType.CANCEL_COMMAND => new CancelCommandDetectedEvent(Guid.NewGuid(), DateTime.UtcNow, message.Id.ToString(), "RULE_BASED", extractedDataJson),
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
