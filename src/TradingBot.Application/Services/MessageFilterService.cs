using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Telegram.Models;

namespace TradingBot.Application.Services;

public class MessageFilterService : IMessageFilter
{
    private readonly ILogger<MessageFilterService> _logger;
    private readonly SignalDetectionSettings _settings;

    public MessageFilterService(
        ILogger<MessageFilterService> logger,
        IOptions<SignalDetectionSettings> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = options?.Value ?? new SignalDetectionSettings();
    }

    public Task<SignalCandidate?> AnalyzeAsync(TelegramMessageDto message)
    {
        try
        {
            // 1. Basic Validation
            if (message == null)
            {
                _logger.LogWarning("Message Filter: Received null message DTO.");
                return Task.FromResult<SignalCandidate?>(null);
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                _logger.LogDebug("Message Filter: Message text is empty or whitespace. MessageId: {MessageId}", message.MessageId);
                return Task.FromResult<SignalCandidate?>(null);
            }

            var text = message.Text;

            // 2. Aggregate Keywords from Configured Languages
            var longKeywords = new List<string>();
            var shortKeywords = new List<string>();
            var priceKeywords = new List<string>();
            var riskKeywords = new List<string>();

            AggregateRules(_settings.DetectionRules?.English);
            AggregateRules(_settings.DetectionRules?.Persian);
            AggregateRules(_settings.DetectionRules?.Custom);

            longKeywords = Deduplicate(longKeywords);
            shortKeywords = Deduplicate(shortKeywords);
            priceKeywords = Deduplicate(priceKeywords);
            riskKeywords = riskKeywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); // Deduplicate SL/TP keywords

            void AggregateRules(LanguageRules? rules)
            {
                if (rules == null) return;
                if (rules.LongKeywords != null) longKeywords.AddRange(rules.LongKeywords);
                if (rules.ShortKeywords != null) shortKeywords.AddRange(rules.ShortKeywords);
                if (rules.PriceKeywords != null) priceKeywords.AddRange(rules.PriceKeywords);
                if (rules.RiskKeywords != null) riskKeywords.AddRange(rules.RiskKeywords);
            }

            List<string> Deduplicate(List<string> list) =>
                list.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            // 3. Symbol Detection
            string? detectedSymbol = null;
            var allSymbolKeys = new List<string>();
            if (_settings.SupportedSymbols != null)
            {
                allSymbolKeys.AddRange(_settings.SupportedSymbols);
            }
            if (_settings.SymbolAliases != null)
            {
                allSymbolKeys.AddRange(_settings.SymbolAliases.Keys);
            }

            var sortedSymbolKeys = allSymbolKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Length)
                .ToList();

            foreach (var key in sortedSymbolKeys)
            {
                if (ContainsKeyword(text, key))
                {
                    if (_settings.SymbolAliases != null && _settings.SymbolAliases.TryGetValue(key, out var mappedSymbol))
                    {
                        detectedSymbol = mappedSymbol;
                    }
                    else
                    {
                        detectedSymbol = key.ToUpperInvariant();
                    }

                    _logger.LogInformation("Symbol detected: {Symbol}", detectedSymbol);
                    break;
                }
            }

            // 4. Direction Detection
            string? detectedSide = null;
            int earliestLongIndex = int.MaxValue;
            int earliestShortIndex = int.MaxValue;

            foreach (var kw in longKeywords)
            {
                if (ContainsKeyword(text, kw))
                {
                    int idx = text.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && idx < earliestLongIndex)
                    {
                        earliestLongIndex = idx;
                    }
                }
            }

            foreach (var kw in shortKeywords)
            {
                if (ContainsKeyword(text, kw))
                {
                    int idx = text.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && idx < earliestShortIndex)
                    {
                        earliestShortIndex = idx;
                    }
                }
            }

            if (earliestLongIndex < int.MaxValue || earliestShortIndex < int.MaxValue)
            {
                if (earliestLongIndex <= earliestShortIndex)
                {
                    detectedSide = "LONG";
                }
                else
                {
                    detectedSide = "SHORT";
                }
            }

            // 5. Keyword Presence (Price and Risk Indicators)
            bool hasPriceKeyword = false;
            foreach (var kw in priceKeywords)
            {
                if (ContainsKeyword(text, kw))
                {
                    hasPriceKeyword = true;
                    break;
                }
            }

            bool hasRiskKeyword = false;
            foreach (var kw in riskKeywords)
            {
                if (ContainsKeyword(text, kw))
                {
                    hasRiskKeyword = true;
                    break;
                }
            }

            // 6. Score Calculation
            int score = 0;
            if (detectedSymbol != null) score += 30;
            if (detectedSide != null) score += 30;
            if (hasPriceKeyword) score += 20;
            if (hasRiskKeyword) score += 20;

            // 7. Filtering Decision
            if (score >= _settings.MinimumScore)
            {
                _logger.LogInformation("Signal candidate detected. Channel: {ChannelName}, Score: {Score}", message.ChannelName, score);

                var candidate = new SignalCandidate
                {
                    ChannelId = message.ChannelId,
                    MessageId = message.MessageId,
                    RawText = message.Text,
                    DetectedSymbol = detectedSymbol ?? string.Empty,
                    DetectedSide = detectedSide ?? string.Empty,
                    DetectionScore = score,
                    DetectedAt = DateTime.UtcNow
                };

                return Task.FromResult<SignalCandidate?>(candidate);
            }

            _logger.LogInformation("Message ignored. MessageId: {MessageId}, Score: {Score}", message.MessageId, score);
            return Task.FromResult<SignalCandidate?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message Filter: Error occurred while analyzing message. MessageId: {MessageId}", message?.MessageId);
            return Task.FromResult<SignalCandidate?>(null); // Must never crash receiver worker
        }
    }

    private bool ContainsKeyword(string text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
            return false;

        // Check if keyword is entirely alphanumeric (ignoring whitespace).
        // If it contains only letters, digits or spaces, we can use regex with word boundary \b.
        // Emojis/special characters do not work well with \b word boundaries.
        bool isAlphaNumeric = true;
        foreach (char c in keyword)
        {
            if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))
            {
                isAlphaNumeric = false;
                break;
            }
        }

        if (isAlphaNumeric)
        {
            var pattern = $@"\b{Regex.Escape(keyword)}\b";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }
        else
        {
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
