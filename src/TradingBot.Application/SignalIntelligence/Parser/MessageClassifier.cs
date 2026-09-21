using System;
using System.Collections.Generic;
using System.Linq;
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

        var rawText = _preprocessor.Preprocess(message.Content);
        var normalized = NormalizeText(rawText);
        var upper = normalized.ToUpperInvariant();

        MessageType type = MessageType.UNKNOWN;
        TelegramMessageIntent intent = TelegramMessageIntent.Unknown;
        string? action = null;
        string? targetSymbol = ExtractSymbol(normalized);
        decimal confidence = 0.0m;
        var extractedObj = new Dictionary<string, object>();

        // 1. Check for conditional or disclaimer/advice phrasing first (e.g. #48285 "تا قبل از تارگت اول...")
        bool isConditionalAdvice = upper.Contains("تا قبل از") || upper.Contains("نکته مهم") ||
                                   upper.Contains("مجاز به") || upper.Contains("قوانین") ||
                                   (upper.Contains("اگر") && upper.Contains("مجاز")) ||
                                   (upper.Contains("اگه") && upper.Contains("میزنم"));

        // 2. Check for market commentary / price reaction phrasing (e.g. #48295 "دقیقا به 1.14720 واکنش داده...")
        bool isMarketCommentary = upper.Contains("واکنش داده") || upper.Contains("فعال نشدیم") ||
                                  upper.Contains("باز نشده برامون") || upper.Contains("معامله هنوز باز نشده") ||
                                  upper.Contains("تحلیل") || upper.Contains("چارت") || upper.Contains("منتظریم") ||
                                  upper.Contains("منتظر") || upper.Contains("رنج") || upper.Contains("ریزش") ||
                                  upper.Contains("احتمال") || upper.Contains("ANALYSIS") || upper.Contains("MARKET");

        // 3. Check for status report / performance
        bool isStatusReport = upper.Contains("گزارش") || upper.Contains("سود روزانه") || upper.Contains("STATUS") ||
                              upper.Contains("PERFORMANCE") || upper.Contains("DAILY PROFIT") || upper.Contains("WEEKLY PROFIT");

        if (isConditionalAdvice)
        {
            type = MessageType.STATUS_UPDATE;
            intent = TelegramMessageIntent.Informational;
            action = "None";
            confidence = 0.90m;
        }
        else if (isMarketCommentary)
        {
            type = MessageType.ANALYSIS;
            intent = TelegramMessageIntent.MarketAnalysis;
            action = "None";
            confidence = 0.90m;
        }
        else if (isStatusReport)
        {
            type = MessageType.STATUS_UPDATE;
            intent = TelegramMessageIntent.Informational;
            action = "None";
            confidence = 0.85m;
        }
        else
        {
            // Cancel Commands
            bool isCancelCommand = upper.Contains("CANCEL") || upper.Contains("کنسل") || upper.Contains("لغو");

            // Position Management & Trade Updates
            bool isRiskFree = upper.Contains("RISK FREE") || upper.Contains("RISKFREE") || upper.Contains("BREAKEVEN") ||
                              upper.Contains("BREAK EVEN") || upper.Contains("ریسک فری") || upper.Contains("سر به سر") ||
                              upper.Contains("فری کنید") || upper.Contains("حد ضرر منتقل");

            bool isPartialClose = upper.Contains("CLOSE PARTIAL") || upper.Contains("CLOSE HALF") || upper.Contains("سیو سود") ||
                                  upper.Contains("بخشی از معامله") || upper.Contains("بخشی از حجم") ||
                                  upper.Contains("TP1 HIT") || upper.Contains("TP2 HIT") || upper.Contains("TARGET 1 HIT") ||
                                  upper.Contains("TARGET 2 HIT") || upper.Contains("تارگت اول خورد") || upper.Contains("تارگت دوم خورد") ||
                                  upper.Contains("تارگت 1 خورد") || upper.Contains("تارگت 2 خورد") || upper.Contains("تارگت اول رسید") ||
                                  upper.Contains("تارگت 1 رسید") || upper.Contains("تارگت۱ خورد");

            bool isUpdateSl = upper.Contains("UPDATE SL") || upper.Contains("MOVE SL") || upper.Contains("MOVE STOP") || upper.Contains("تغییر حد ضرر");
            bool isUpdateTp = upper.Contains("UPDATE TP") || upper.Contains("تغییر حد سود");

            bool isCloseAll = upper.Contains("CLOSE ALL") || upper.Contains("همه معاملات") || upper.Contains("تمام معاملات") || upper.Contains("همه اوردرها");
            bool isClosePosition = upper.Contains("CLOSE POSITION") || upper.Contains("EXIT NOW") || upper.Contains("ببندید") ||
                                   upper.Contains("خروج کامل") || upper.Contains("معامله بسته شد") || upper.Contains("معامله بسته شود") ||
                                   upper.Contains("پوزیشن بسته شد") || upper.Contains("پوزیشن بسته شود");

            if (isCancelCommand)
            {
                type = MessageType.CANCEL_COMMAND;
                intent = TelegramMessageIntent.ExitSignal;
                confidence = 0.95m;
                action = isCloseAll || upper.Contains("ALL") || upper.Contains("همه") ? "CloseAllPositions" : "CancelOrder";
            }
            else if (isRiskFree || isPartialClose || isUpdateSl || isUpdateTp)
            {
                type = MessageType.TRADE_UPDATE;
                intent = TelegramMessageIntent.PositionManagement;
                confidence = 0.95m;

                if (isRiskFree) action = "MoveStopLossToEntry";
                else if (isPartialClose) action = "TakePartialProfit";
                else if (isUpdateSl) action = "UpdateStopLoss";
                else if (isUpdateTp) action = "UpdateTakeProfit";
            }
            else if (isCloseAll || isClosePosition)
            {
                type = MessageType.TRADE_UPDATE;
                intent = TelegramMessageIntent.ExitSignal;
                confidence = 0.95m;
                action = isCloseAll ? "CloseAllPositions" : "ClosePosition";
            }
            // Structured Entry Signal
            else
            {
                bool hasDirection = upper.Contains("BUY") || upper.Contains("SELL") || upper.Contains("LONG") ||
                                    upper.Contains("SHORT") || upper.Contains("خرید") || upper.Contains("فروش") ||
                                    upper.Contains("شراء");

                bool hasSignalKeywords = upper.Contains("ENTRY") || upper.Contains("ENT") || upper.Contains("BUY ZONE") ||
                                         upper.Contains("SL") || upper.Contains("TP") || upper.Contains("TARGET") ||
                                         upper.Contains("STOP LOSS") || upper.Contains("TAKE PROFIT") ||
                                         upper.Contains("ورود") || upper.Contains("حد سود") || upper.Contains("حد ضرر") ||
                                         upper.Contains("تارگت") || upper.Contains("@") || upper.Contains("LEVERAGE") ||
                                         upper.Contains("X");

                if (!string.IsNullOrEmpty(targetSymbol) && (hasDirection || hasSignalKeywords))
                {
                    type = MessageType.SIGNAL;
                    intent = TelegramMessageIntent.EntrySignal;
                    action = "None";
                    confidence = 0.92m;
                }
                else if (upper.Contains("HELLO") || upper.Contains("WELCOME") || upper.Contains("CHAT") ||
                         upper.Contains("ADMIN") || upper.Contains("SUPPORT") || upper.Contains("سلام") ||
                         upper.Contains("خوش آمدید") || upper.Contains("پشتیبانی") || upper.Contains("کانال"))
                {
                    type = MessageType.GENERAL_MESSAGE;
                    intent = TelegramMessageIntent.Noise;
                    action = "None";
                    confidence = 0.80m;
                }
                else
                {
                    type = MessageType.UNKNOWN;
                    intent = TelegramMessageIntent.Unknown;
                    action = "None";
                    confidence = 0.0m;
                }
            }
        }

        extractedObj["type"] = type.ToString();
        extractedObj["intent"] = intent.ToString();
        if (action != null) extractedObj["action"] = action;
        if (targetSymbol != null) extractedObj["symbol"] = targetSymbol;
        extractedObj["confidence"] = confidence;

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        string extractedDataJson = JsonSerializer.Serialize(extractedObj, jsonOptions);

        var processingStatus = intent switch
        {
            TelegramMessageIntent.EntrySignal => "Completed",
            TelegramMessageIntent.PositionManagement => "Completed",
            TelegramMessageIntent.ExitSignal => "Completed",
            _ => "Filtered"
        };

        var analysis = new MessageAnalysis(
            message.Id,
            type,
            confidence,
            extractedDataJson,
            aiUsed: false,
            DateTime.UtcNow,
            intent: intent,
            action: action,
            targetSymbol: targetSymbol,
            processingStatus: processingStatus,
            extractedMetadata: extractedDataJson
        );

        return Task.FromResult(analysis);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        char[] persianDigits = { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };
        char[] arabicDigits = { '٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩' };
        for (int i = 0; i < 10; i++)
        {
            text = text.Replace(persianDigits[i], (char)('0' + i));
            text = text.Replace(arabicDigits[i], (char)('0' + i));
        }

        text = text.Replace('ك', 'ک').Replace('ي', 'ی');
        return text.Trim();
    }

    private static string? ExtractSymbol(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var upper = text.ToUpperInvariant();

        if (upper.Contains("بیت کوین") || upper.Contains("بیتکوین") || upper.Contains("BITCOIN") || Regex.IsMatch(upper, @"\bبیت\b"))
        {
            return "BTCUSDT";
        }
        if (upper.Contains("اتریوم") || upper.Contains("ETHEREUM"))
        {
            return "ETHUSDT";
        }
        if (upper.Contains("یورو")) return "EURUSD";
        if (upper.Contains("پوند")) return "GBPUSD";
        if (upper.Contains("طلا") || upper.Contains("انس")) return "XAUUSD";
        if (Regex.IsMatch(upper, @"\bین\b")) return "USDJPY";
        if (upper.Contains("فرانک")) return "USDCHF";
        if (upper.Contains("کاد")) return "USDCAD";
        if (upper.Contains("استرالیا")) return "AUDUSD";

        var pairMatch = Regex.Match(upper, @"\b([A-Z]{3,6})[/|-]([A-Z]{3,4})\b");
        if (pairMatch.Success)
        {
            var baseC = pairMatch.Groups[1].Value;
            var quoteC = pairMatch.Groups[2].Value;
            return $"{baseC}{quoteC}";
        }

        var match = Regex.Match(upper, @"\b([A-Z0-9]{3,8}(?:USDT|USDC|BUSD)?)\b");
        while (match.Success)
        {
            var val = match.Groups[1].Value.Replace("/", "").Replace("-", "");
            if (val.Length >= 6 && !val.All(char.IsDigit) && !ExcludedKeywords.Contains(val))
            {
                return val;
            }
            match = match.NextMatch();
        }

        return null;
    }

    private static readonly HashSet<string> ExcludedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CANCEL", "CLOSE", "UPDATE", "BUY", "SELL", "STOP", "LIMIT", "ENTRY", "RISK", "FREE",
        "HALF", "NOW", "PARTIAL", "ALL", "ORDER", "ORDERS", "SL", "TP", "INFO", "LONG", "SHORT"
    };
}
