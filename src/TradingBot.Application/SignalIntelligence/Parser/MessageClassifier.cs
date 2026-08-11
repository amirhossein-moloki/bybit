using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Application.SignalIntelligence.Parser;

public class MessageClassifier : IMessageClassifier
{
    private readonly IMessagePreprocessor _preprocessor;

    public MessageClassifier(IMessagePreprocessor preprocessor)
    {
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
    }

    public Task<MessageAnalysis> ClassifyAsync(TelegramMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var normalized = _preprocessor.Preprocess(message.Content);
        var upper = normalized.ToUpperInvariant();

        MessageType type = MessageType.UNKNOWN;
        decimal confidence = 0.0m;
        var extractedObj = new System.Collections.Generic.Dictionary<string, object>();

        // 1. Check for CANCEL_COMMAND
        if (upper.Contains("CANCEL ALL") || upper.Contains("کنسل") || upper.Contains("لغو") || upper.Contains("CANCEL ORDER") ||
            Regex.IsMatch(upper, @"\b([A-Z0-9]{3,8}(?:USDT|USDC)?)\s+CANCEL\b") ||
            Regex.IsMatch(upper, @"\bCANCEL\s+([A-Z0-9]{3,8}(?:USDT|USDC)?)\b"))
        {
            type = MessageType.CANCEL_COMMAND;
            confidence = 0.95m;
            extractedObj["type"] = "CANCEL_COMMAND";
            extractedObj["action"] = "CANCEL_ALL";

            // Extract symbol if present
            var symMatch = Regex.Match(upper, @"\b([A-Z0-9]{3,8}(?:USDT|USDC)?)\s+CANCEL\b");
            if (!symMatch.Success)
            {
                symMatch = Regex.Match(upper, @"\bCANCEL\s+([A-Z0-9]{3,8}(?:USDT|USDC)?)\b");
            }
            if (symMatch.Success)
            {
                extractedObj["symbol"] = symMatch.Groups[1].Value.Replace("/", "").Replace("-", "");
                extractedObj["action"] = "CANCEL_SYMBOL";
            }
        }
        // 2. Check for TRADE_UPDATE
        else if (upper.Contains("RISK FREE") || upper.Contains("MOVE STOP") || upper.Contains("MOVE SL") ||
                 upper.Contains("CLOSE PARTIAL") || upper.Contains("CLOSE HALF") || upper.Contains("CLOSE POSITION") ||
                 upper.Contains("EXIT NOW") || upper.Contains("ریسک فری") || upper.Contains("سیو سود") ||
                 upper.Contains("ببندید") || upper.Contains("خروج") || upper.Contains("فری کنید") ||
                 upper.Contains("UPDATE SL") || upper.Contains("UPDATE TP") || upper.Contains("تغییر حد ضرر") || upper.Contains("تغییر حد سود"))
        {
            type = MessageType.TRADE_UPDATE;
            confidence = 0.90m;
            extractedObj["type"] = "TRADE_UPDATE";

            if (upper.Contains("RISK FREE") || upper.Contains("ریسک فری") || upper.Contains("فری کنید"))
            {
                extractedObj["action"] = "MOVE_STOP_TO_ENTRY";
            }
            else if (upper.Contains("CLOSE PARTIAL") || upper.Contains("CLOSE HALF") || upper.Contains("سیو سود"))
            {
                extractedObj["action"] = "CLOSE_PARTIAL";
            }
            else if (upper.Contains("CLOSE POSITION") || upper.Contains("EXIT NOW") || upper.Contains("ببندید") || upper.Contains("خروج"))
            {
                extractedObj["action"] = "CLOSE_POSITION";
            }
            else if (upper.Contains("UPDATE SL") || upper.Contains("تغییر حد ضرر"))
            {
                extractedObj["action"] = "UPDATE_STOP_LOSS";
            }
            else if (upper.Contains("UPDATE TP") || upper.Contains("تغییر حد سود"))
            {
                extractedObj["action"] = "UPDATE_TAKE_PROFIT";
            }
            else
            {
                extractedObj["action"] = "UNKNOWN";
            }
        }
        // 3. Check for STATUS_UPDATE
        else if (upper.Contains("STATUS") || upper.Contains("PERFORMANCE") || upper.Contains("DAILY PROFIT") ||
                 upper.Contains("WEEKLY PROFIT") || upper.Contains("گزارش") || upper.Contains("سود روزانه") ||
                 upper.Contains("سود هفتگی") || upper.Contains("سود ماهانه"))
        {
            type = MessageType.STATUS_UPDATE;
            confidence = 0.85m;
            extractedObj["type"] = "STATUS_UPDATE";
        }
        // 4. Check for ANALYSIS
        else if (upper.Contains("ANALYSIS") || upper.Contains("CHART") || upper.Contains("MARKET") ||
                 upper.Contains("FORECAST") || upper.Contains("TREND") || upper.Contains("تحلیل") ||
                 upper.Contains("چارت") || upper.Contains("رنج") || upper.Contains("ریزش") ||
                 upper.Contains("منتظر") || upper.Contains("احتمال") || upper.Contains("بازار") ||
                 upper.Contains("PREDICTION"))
        {
            type = MessageType.ANALYSIS;
            confidence = 0.80m;
            extractedObj["type"] = "ANALYSIS";
        }
        // 5. Check for GENERAL_MESSAGE
        else if (upper.Contains("HELLO") || upper.Contains("WELCOME") || upper.Contains("CHAT") ||
                 upper.Contains("ADMIN") || upper.Contains("SUPPORT") || upper.Contains("سلام") ||
                 upper.Contains("خوش آمدید") || upper.Contains("پشتیبانی") || upper.Contains("کانال"))
        {
            type = MessageType.GENERAL_MESSAGE;
            confidence = 0.75m;
            extractedObj["type"] = "GENERAL_MESSAGE";
        }
        // 6. Check for SIGNAL
        else
        {
            // A signal must contain a potential symbol AND direction keyword
            bool hasSymbol = Regex.IsMatch(upper, @"\b([A-Z0-9]{3,8}(?:/|-|_)?(?:USDT|USDC|BUSD)?)\b") ||
                             Regex.IsMatch(upper, @"\b([A-Z0-9]{3,8})\b");

            bool hasDirection = upper.Contains("BUY") || upper.Contains("SELL") || upper.Contains("LONG") ||
                                upper.Contains("SHORT") || upper.Contains("خرید") || upper.Contains("فروش") ||
                                upper.Contains("شراء");

            bool hasSignalKeywords = upper.Contains("ENTRY") || upper.Contains("ENT") || upper.Contains("BUY ZONE") ||
                                     upper.Contains("SL") || upper.Contains("TP") || upper.Contains("TARGET") ||
                                     upper.Contains("STOP LOSS") || upper.Contains("TAKE PROFIT") ||
                                     upper.Contains("ورود") || upper.Contains("حد سود") || upper.Contains("حد ضرر") ||
                                     upper.Contains("تارگت") || upper.Contains("@") || upper.Contains("LEVERAGE") ||
                                     upper.Contains("X");

            if (hasSymbol && hasDirection && hasSignalKeywords)
            {
                type = MessageType.SIGNAL;
                confidence = 0.90m; // Default starting confidence for highly likely signals
                extractedObj["type"] = "SIGNAL";
            }
            else
            {
                type = MessageType.UNKNOWN;
                confidence = 0.0m;
                extractedObj["type"] = "UNKNOWN";
            }
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        string extractedDataJson = JsonSerializer.Serialize(extractedObj, jsonOptions);

        var analysis = new MessageAnalysis(message.Id, type, confidence, extractedDataJson, aiUsed: false, DateTime.UtcNow);
        return Task.FromResult(analysis);
    }
}
