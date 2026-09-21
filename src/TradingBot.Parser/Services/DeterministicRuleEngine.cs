using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;

namespace TradingBot.Parser.Services;

public class DeterministicRuleEngine : IDeterministicRuleEngine
{
    private readonly ILogger<DeterministicRuleEngine> _logger;

    public DeterministicRuleEngine(ILogger<DeterministicRuleEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<StructuredIntent?> EvaluateAsync(MessageIntelligenceContext context, CancellationToken cancellationToken = default)
    {
        if (context == null || context.CurrentMessage == null || string.IsNullOrWhiteSpace(context.CurrentMessage.Text))
        {
            return Task.FromResult<StructuredIntent?>(null);
        }

        var text = NormalizeText(context.CurrentMessage.Text);

        // 1. Check Clearly Fast-Path Imperative Commands first
        if (IsRiskFreeCommand(text, context, out var rfIntent))
        {
            return Task.FromResult<StructuredIntent?>(rfIntent);
        }

        if (IsClosePositionCommand(text, context, out var closeIntent))
        {
            return Task.FromResult<StructuredIntent?>(closeIntent);
        }

        if (IsCancelOrdersCommand(text, context, out var cancelIntent))
        {
            return Task.FromResult<StructuredIntent?>(cancelIntent);
        }

        if (IsRestoreOrdersCommand(text, context, out var restoreIntent))
        {
            return Task.FromResult<StructuredIntent?>(restoreIntent);
        }

        if (IsReEntryCommand(text, context, out var reEntryIntent))
        {
            return Task.FromResult<StructuredIntent?>(reEntryIntent);
        }

        // 2. Check Commentary or Questions
        if (IsCommentaryOrQuestion(text, context, out var commentaryIntent))
        {
            return Task.FromResult<StructuredIntent?>(commentaryIntent);
        }

        // 3. Check Informational Status Reports
        if (IsStatusReport(text, context, out var statusIntent))
        {
            return Task.FromResult<StructuredIntent?>(statusIntent);
        }

        return Task.FromResult<StructuredIntent?>(null);
    }

    private string NormalizeText(string text)
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

    private bool IsRiskFreeCommand(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        bool hasRiskFreeKeyword = Regex.IsMatch(text, @"(?:ریسک\s*فری|risk\s*free|breakeven|break\s*even)", RegexOptions.IgnoreCase);
        bool hasCommandAction = Regex.IsMatch(text, @"(?:کنید|کنید.|کنین|بکنید|شود|set|move)", RegexOptions.IgnoreCase) ||
                                 text.Equals("risk free", StringComparison.OrdinalIgnoreCase) ||
                                 text.Equals("breakeven", StringComparison.OrdinalIgnoreCase) ||
                                 text.Equals("ریسک فری", StringComparison.OrdinalIgnoreCase) ||
                                 text.Equals("ریسک فری کنید", StringComparison.OrdinalIgnoreCase);

        if (hasRiskFreeKeyword && hasCommandAction)
        {
            bool isAll = Regex.IsMatch(text, @"(?:همه|تمام|all)", RegexOptions.IgnoreCase);
            bool hasSymbol = ExtractSymbol(text, context, out var symbol);

            IntentScope scope = IntentScope.SIGNAL;
            var targets = new List<string>();

            if (isAll)
            {
                scope = IntentScope.ALL_ACTIVE_TRADES;
            }
            else if (hasSymbol && symbol != null)
            {
                scope = IntentScope.SYMBOL;
                targets.Add(symbol);
            }
            else if (context.ReplyContext?.Symbol != null)
            {
                scope = IntentScope.SIGNAL;
                targets.Add(context.ReplyContext.Symbol);
            }
            else if (context.ActivePositions.Count == 1)
            {
                scope = IntentScope.SYMBOL;
                targets.Add(context.ActivePositions.First().Symbol);
            }
            else
            {
                scope = IntentScope.SYMBOL;
            }

            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.COMMAND,
                Intent = isAll ? TradingIntent.RISK_FREE_ALL : TradingIntent.RISK_FREE,
                Scope = scope,
                Targets = targets,
                Confidence = 0.96m,
                RequiresExecution = true,
                IsTradeRelated = true,
                IsExecutableCandidate = true,
                ExecutionMode = ExecutionMode.Immediate,
                ResolutionStatus = ResolutionStatus.Resolved,
                Reason = "Deterministic fast-path matched Risk Free command.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool IsClosePositionCommand(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        bool hasCloseKeyword = Regex.IsMatch(text, @"(?:ببندید|ببندین|ببند|ببندید.|close|exit)", RegexOptions.IgnoreCase);
        if (hasCloseKeyword && !text.Contains("اوردر"))
        {
            bool isAll = Regex.IsMatch(text, @"(?:همه|تمام|all)", RegexOptions.IgnoreCase);
            bool hasSymbol = ExtractSymbol(text, context, out var symbol);

            IntentScope scope = isAll ? IntentScope.ALL_ACTIVE_TRADES : IntentScope.POSITION;
            var targets = new List<string>();

            if (hasSymbol && symbol != null)
            {
                scope = IntentScope.SYMBOL;
                targets.Add(symbol);
            }
            else if (context.ReplyContext?.Symbol != null)
            {
                targets.Add(context.ReplyContext.Symbol);
            }

            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.COMMAND,
                Intent = isAll ? TradingIntent.CLOSE_ALL : TradingIntent.CLOSE,
                Scope = scope,
                Targets = targets,
                Confidence = 0.95m,
                RequiresExecution = true,
                IsTradeRelated = true,
                IsExecutableCandidate = true,
                ExecutionMode = ExecutionMode.Immediate,
                ResolutionStatus = ResolutionStatus.Resolved,
                Reason = "Deterministic fast-path matched Close Position command.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool IsCancelOrdersCommand(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        bool hasCancelKeyword = Regex.IsMatch(text, @"(?:کنسل|لغو|cancel)", RegexOptions.IgnoreCase);
        bool hasOrdersKeyword = Regex.IsMatch(text, @"(?:اوردر|سفارش|orders?|pending)", RegexOptions.IgnoreCase) ||
                                text.Contains("فعال نشدن") ||
                                text.Contains("تایم خبر");

        if (hasCancelKeyword || (hasOrdersKeyword && Regex.IsMatch(text, @"کنسل|لغو", RegexOptions.IgnoreCase)))
        {
            bool isAll = Regex.IsMatch(text, @"(?:همه|تمام|all|فعال\s*نشدن)", RegexOptions.IgnoreCase);
            bool hasSymbol = ExtractSymbol(text, context, out var symbol);

            IntentScope scope = IntentScope.ALL_PENDING_ORDERS;
            var targets = new List<string>();

            if (hasSymbol && symbol != null)
            {
                scope = IntentScope.SYMBOL;
                targets.Add(symbol);
            }
            else if (isAll || text.Contains("اوردر") || text.Contains("اخبار") || text.Contains("تایم خبر"))
            {
                scope = IntentScope.ALL_PENDING_ORDERS;
            }
            else if (context.ReplyContext?.Symbol != null)
            {
                scope = IntentScope.SIGNAL;
                targets.Add(context.ReplyContext.Symbol);
            }

            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.COMMAND,
                Intent = TradingIntent.CANCEL_PENDING_ORDERS,
                Scope = scope,
                Targets = targets,
                Confidence = 0.97m,
                RequiresExecution = true,
                IsTradeRelated = true,
                IsExecutableCandidate = true,
                ExecutionMode = ExecutionMode.Immediate,
                ResolutionStatus = ResolutionStatus.Resolved,
                Reason = "Deterministic fast-path matched Cancel Orders command.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool IsRestoreOrdersCommand(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        if (Regex.IsMatch(text, @"(?:اوردر\s*ها\s*رو\s*برگردونید|restore\s*orders|re-enable\s*orders)", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.COMMAND,
                Intent = TradingIntent.RESTORE_ORDER,
                Scope = IntentScope.ALL_PENDING_ORDERS,
                Targets = new List<string>(),
                Confidence = 0.94m,
                RequiresExecution = true,
                IsTradeRelated = true,
                IsExecutableCandidate = true,
                ExecutionMode = ExecutionMode.Immediate,
                ResolutionStatus = ResolutionStatus.Resolved,
                Reason = "Deterministic fast-path matched Restore Orders command.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool IsReEntryCommand(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        if (Regex.IsMatch(text, @"(?:ورود\s*مجدد|re-entry|reentry)", RegexOptions.IgnoreCase))
        {
            ExtractSymbol(text, context, out var sym);

            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.SIGNAL_UPDATE,
                Intent = TradingIntent.RE_ENTRY,
                Scope = sym != null ? IntentScope.SYMBOL : IntentScope.SIGNAL,
                Targets = sym != null ? new List<string> { sym } : new List<string>(),
                Confidence = 0.93m,
                RequiresExecution = true,
                IsTradeRelated = true,
                IsExecutableCandidate = true,
                ExecutionMode = ExecutionMode.Immediate,
                ResolutionStatus = ResolutionStatus.Resolved,
                Reason = "Deterministic fast-path matched Re-entry signal update.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool IsCommentaryOrQuestion(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        if (Regex.IsMatch(text, @"(?:چهارتا|چندتا|\d+)\s*اوردر\s*داریم|منتظریم", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.COMMENTARY,
                Intent = TradingIntent.NO_ACTION,
                Scope = IntentScope.NONE,
                Targets = new List<string>(),
                Confidence = 0.95m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message is channel market commentary.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        if (Regex.IsMatch(text, @"(?:معتبره|معتبر\s*است|valid)", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.COMMENTARY,
                Intent = TradingIntent.NO_ACTION,
                Scope = ExtractSymbol(text, context, out var sym) ? IntentScope.SYMBOL : IntentScope.SIGNAL,
                Targets = sym != null ? new List<string> { sym } : new List<string>(),
                Confidence = 0.92m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message is signal validation commentary.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        if (Regex.IsMatch(text, @"وضعیت\s*معاملات|\bstatus\b", RegexOptions.IgnoreCase) && Regex.IsMatch(text, @"اعلام|چطوره|\?", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.QUESTION,
                Intent = TradingIntent.NO_ACTION,
                Scope = IntentScope.NONE,
                Targets = new List<string>(),
                Confidence = 0.94m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message is a status question/request to members.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool IsStatusReport(string text, MessageIntelligenceContext context, out StructuredIntent? intent)
    {
        intent = null;

        if (Regex.IsMatch(text, @"(?:ریسک\s*فری\s*(?:شد|شده|شدیم|اعلام)|risk\s*free\s*done|breakeven\s*done)", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.STATUS,
                Intent = TradingIntent.STATUS_REPORT,
                Scope = ExtractSymbol(text, context, out var sym) ? IntentScope.SYMBOL : IntentScope.ALL_ACTIVE_TRADES,
                Targets = sym != null ? new List<string> { sym } : new List<string>(),
                Confidence = 0.98m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message reports risk free status reached.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        if (Regex.IsMatch(text, @"^\s*(?:فعاله|فعال\s*شد|active)\s*$", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.STATUS,
                Intent = TradingIntent.STATUS_REPORT,
                Scope = IntentScope.SIGNAL,
                Targets = context.ReplyContext?.Symbol != null ? new List<string> { context.ReplyContext.Symbol } : new List<string>(),
                Confidence = 0.95m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message reports trade or signal active status.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        if (Regex.IsMatch(text, @"(?:تارگت|target|tp).*?✅", RegexOptions.IgnoreCase) || Regex.IsMatch(text, @"(?:تارگت|target|tp)\s*(?:[1-9]|اول|دوم|سوم|چهارم)\s*(?:شد|رسید|hit)?", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.STATUS,
                Intent = TradingIntent.STATUS_REPORT,
                Scope = ExtractSymbol(text, context, out var sym) ? IntentScope.SYMBOL : IntentScope.SIGNAL,
                Targets = sym != null ? new List<string> { sym } : new List<string>(),
                Confidence = 0.96m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message reports take profit target hit status.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        if (Regex.IsMatch(text, @"(?:استاپ\s*شد|حد\s*ضرر\s*خورد|stop\s*loss\s*hit|sl\s*hit)", RegexOptions.IgnoreCase))
        {
            intent = new StructuredIntent
            {
                MessageType = IntelligenceMessageType.STATUS,
                Intent = TradingIntent.STATUS_REPORT,
                Scope = ExtractSymbol(text, context, out var sym) ? IntentScope.SYMBOL : IntentScope.POSITION,
                Targets = sym != null ? new List<string> { sym } : new List<string>(),
                Confidence = 0.98m,
                RequiresExecution = false,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                ExecutionMode = ExecutionMode.Informational,
                ResolutionStatus = ResolutionStatus.NotTradingRelated,
                Reason = "Message reports stop loss hit status.",
                DetectionMethod = "RULE"
            };
            return true;
        }

        return false;
    }

    private bool ExtractSymbol(string text, MessageIntelligenceContext context, out string? symbol)
    {
        symbol = null;

        var symbolMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "یورو", "EURUSD" },
            { "پوند", "GBPUSD" },
            { "طلا", "XAUUSD" },
            { "انس", "XAUUSD" },
            { "انسن", "XAUUSD" },
            { "ین", "USDJPY" },
            { "فرانک", "USDCHF" },
            { "کاد", "USDCAD" },
            { "استرالیا", "AUDUSD" },
            { "بیتکوین", "BTCUSDT" },
            { "بیت کوین", "BTCUSDT" },
            { "بیت", "BTCUSDT" },
            { "اتریوم", "ETHUSDT" }
        };

        foreach (var kvp in symbolMap)
        {
            if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                symbol = kvp.Value;
                return true;
            }
        }

        var match = Regex.Match(text, @"\b([A-Z]{6}|[A-Z]{3}/[A-Z]{3}|BTCUSDT|ETHUSDT|EURUSD|GBPUSD|XAUUSD|USDJPY)\b", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            symbol = match.Value.Replace("/", "").Replace("-", "").ToUpperInvariant();
            return true;
        }

        if (context.ReplyContext?.Symbol != null)
        {
            symbol = context.ReplyContext.Symbol;
            return true;
        }

        return false;
    }
}
