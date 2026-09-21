using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;

namespace TradingBot.Parser.Services;

public class TargetResolver : ITargetResolver
{
    private readonly ILogger<TargetResolver> _logger;

    public TargetResolver(ILogger<TargetResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<StructuredIntent> ResolveTargetsAsync(
        StructuredIntent intent,
        MessageIntelligenceContext context,
        CancellationToken cancellationToken = default)
    {
        if (intent == null) throw new ArgumentNullException(nameof(intent));
        if (context == null) throw new ArgumentNullException(nameof(context));

        bool isTradeRelated = intent.IsTradeRelated || intent.RequiresExecution ||
                             intent.MessageType == IntelligenceMessageType.COMMAND ||
                             intent.MessageType == IntelligenceMessageType.NEW_SIGNAL;

        // If intent does not require execution or is informational / non-trading
        if (!isTradeRelated || intent.Intent == TradingIntent.NO_ACTION || intent.Intent == TradingIntent.UNKNOWN || intent.Intent == TradingIntent.STATUS_REPORT)
        {
            if (intent.ResolutionStatus == ResolutionStatus.Resolved)
            {
                intent.ResolutionStatus = ResolutionStatus.NotTradingRelated;
            }
            return Task.FromResult(intent);
        }

        // Scope: ALL_ACTIVE_TRADES
        if (intent.Scope == IntentScope.ALL_ACTIVE_TRADES)
        {
            var activeSymbols = context.ActivePositions?.Select(p => p.Symbol).Distinct().ToList() ?? new List<string>();
            if (activeSymbols.Any())
            {
                intent.Targets = activeSymbols;
                intent.ResolutionStatus = ResolutionStatus.Resolved;
            }
            else
            {
                intent.ResolutionStatus = ResolutionStatus.InsufficientContext;
                intent.RequiresExecution = false;
                intent.Reason += " [Target Resolution Failed: No active positions exist]";
            }
            return Task.FromResult(intent);
        }

        // Scope: ALL_PENDING_ORDERS
        if (intent.Scope == IntentScope.ALL_PENDING_ORDERS)
        {
            var pendingSymbols = context.PendingOrders?.Select(o => o.Symbol).Distinct().ToList() ?? new List<string>();
            if (pendingSymbols.Any())
            {
                intent.Targets = pendingSymbols;
                intent.ResolutionStatus = ResolutionStatus.Resolved;
            }
            else
            {
                intent.ResolutionStatus = ResolutionStatus.InsufficientContext;
                intent.RequiresExecution = false;
                intent.Reason += " [Target Resolution Failed: No pending orders exist]";
            }
            return Task.FromResult(intent);
        }

        // Check explicit target symbol from intent or Symbol property
        var explicitSymbol = intent.Targets?.FirstOrDefault() ?? intent.Symbol;
        if (!string.IsNullOrWhiteSpace(explicitSymbol))
        {
            intent.Targets = new List<string> { explicitSymbol.ToUpperInvariant() };
            intent.Symbol = explicitSymbol.ToUpperInvariant();
            intent.ResolutionStatus = ResolutionStatus.Resolved;
            return Task.FromResult(intent);
        }

        // Target Resolution Cascade for Implicit Commands (e.g., "ببندش", "ریسک فریش کن")
        var resolvedTargets = new List<string>();

        // 1. Check Reply Context
        if (!string.IsNullOrWhiteSpace(context.ReplyContext?.Symbol))
        {
            var replySym = context.ReplyContext.Symbol.ToUpperInvariant();
            resolvedTargets.Add(replySym);
            intent.Targets = resolvedTargets;
            intent.Symbol = replySym;
            intent.Scope = IntentScope.SYMBOL;
            intent.ResolutionStatus = ResolutionStatus.Resolved;
            _logger.LogInformation("TargetResolver: Resolved target symbol '{Symbol}' from Reply Context for MessageId {MessageId}",
                replySym, context.CurrentMessage.MessageId);
            return Task.FromResult(intent);
        }

        // 2. Check Active Positions
        if (context.ActivePositions != null && context.ActivePositions.Count > 0)
        {
            if (context.ActivePositions.Count == 1)
            {
                var singleSym = context.ActivePositions.First().Symbol.ToUpperInvariant();
                resolvedTargets.Add(singleSym);
                intent.Targets = resolvedTargets;
                intent.Symbol = singleSym;
                intent.Scope = IntentScope.SYMBOL;
                intent.ResolutionStatus = ResolutionStatus.Resolved;
                _logger.LogInformation("TargetResolver: Resolved target symbol '{Symbol}' from unique Active Position for MessageId {MessageId}",
                    singleSym, context.CurrentMessage.MessageId);
                return Task.FromResult(intent);
            }

            // Multiple active positions exist and message has no reply / explicit symbol -> Ambiguous!
            _logger.LogWarning("TargetResolver: MessageId {MessageId} specifies an action but multiple active positions ({Count}) exist without a reply context or symbol. Marking as Ambiguous.",
                context.CurrentMessage.MessageId, context.ActivePositions.Count);

            intent.Targets = new List<string>();
            intent.Symbol = null;
            intent.RequiresExecution = false;
            intent.IsExecutableCandidate = false;
            intent.ResolutionStatus = ResolutionStatus.Ambiguous;
            intent.Reason += $" [Target Resolution Ambiguous: {context.ActivePositions.Count} active positions match]";
            return Task.FromResult(intent);
        }

        // 3. Check Pending Orders
        if (context.PendingOrders != null && context.PendingOrders.Count > 0)
        {
            if (context.PendingOrders.Count == 1)
            {
                var orderSym = context.PendingOrders.First().Symbol.ToUpperInvariant();
                resolvedTargets.Add(orderSym);
                intent.Targets = resolvedTargets;
                intent.Symbol = orderSym;
                intent.Scope = IntentScope.SYMBOL;
                intent.ResolutionStatus = ResolutionStatus.Resolved;
                return Task.FromResult(intent);
            }

            intent.Targets = new List<string>();
            intent.Symbol = null;
            intent.RequiresExecution = false;
            intent.IsExecutableCandidate = false;
            intent.ResolutionStatus = ResolutionStatus.Ambiguous;
            intent.Reason += $" [Target Resolution Ambiguous: {context.PendingOrders.Count} pending orders match]";
            return Task.FromResult(intent);
        }

        // 4. Check Recent Related Signals
        if (context.RelatedSignals != null && context.RelatedSignals.Count == 1)
        {
            var sigSym = context.RelatedSignals.First().Symbol.ToUpperInvariant();
            resolvedTargets.Add(sigSym);
            intent.Targets = resolvedTargets;
            intent.Symbol = sigSym;
            intent.Scope = IntentScope.SYMBOL;
            intent.ResolutionStatus = ResolutionStatus.Resolved;
            return Task.FromResult(intent);
        }

        // Insufficient Context: No matching position, pending order, reply, or signal
        _logger.LogWarning("TargetResolver: Target resolution failed for MessageId {MessageId}. No active positions, signals, or reply references found.",
            context.CurrentMessage.MessageId);

        intent.Targets = new List<string>();
        intent.Symbol = null;
        intent.RequiresExecution = false;
        intent.IsExecutableCandidate = false;
        intent.ResolutionStatus = ResolutionStatus.InsufficientContext;
        intent.Reason += " [Target Resolution Failed: Insufficient Context]";

        return Task.FromResult(intent);
    }
}
