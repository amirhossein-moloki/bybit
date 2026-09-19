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

        // If no execution required or NO_ACTION/UNKNOWN -> no target resolution needed
        if (!intent.RequiresExecution || intent.Intent == TradingIntent.NO_ACTION || intent.Intent == TradingIntent.UNKNOWN)
        {
            return Task.FromResult(intent);
        }

        // If intent scope is ALL_ACTIVE_TRADES or ALL_PENDING_ORDERS -> targets resolved to all active symbols
        if (intent.Scope == IntentScope.ALL_ACTIVE_TRADES)
        {
            var activeSymbols = context.ActivePositions.Select(p => p.Symbol).Distinct().ToList();
            if (activeSymbols.Any())
            {
                intent.Targets = activeSymbols;
            }
            return Task.FromResult(intent);
        }

        if (intent.Scope == IntentScope.ALL_PENDING_ORDERS)
        {
            var pendingSymbols = context.PendingOrders.Select(o => o.Symbol).Distinct().ToList();
            if (pendingSymbols.Any())
            {
                intent.Targets = pendingSymbols;
            }
            return Task.FromResult(intent);
        }

        // If targets are already explicitly populated (e.g. ["EURUSD"]) -> verify against domain context
        if (intent.Targets != null && intent.Targets.Any())
        {
            return Task.FromResult(intent);
        }

        // Target Resolution via Context Cascade
        var resolvedTargets = new List<string>();

        // 1. Check Reply Context
        if (!string.IsNullOrWhiteSpace(context.ReplyContext?.Symbol))
        {
            resolvedTargets.Add(context.ReplyContext.Symbol);
            _logger.LogInformation("TargetResolver: Resolved target symbol '{Symbol}' from Reply Context.", context.ReplyContext.Symbol);
        }
        // 2. Check Active Positions (if single active position exists)
        else if (context.ActivePositions != null && context.ActivePositions.Count == 1)
        {
            var sym = context.ActivePositions.First().Symbol;
            resolvedTargets.Add(sym);
            _logger.LogInformation("TargetResolver: Resolved target symbol '{Symbol}' from single Active Position.", sym);
        }
        // 3. Check Pending Orders (if single pending order exists)
        else if (context.PendingOrders != null && context.PendingOrders.Count == 1)
        {
            var sym = context.PendingOrders.First().Symbol;
            resolvedTargets.Add(sym);
            _logger.LogInformation("TargetResolver: Resolved target symbol '{Symbol}' from single Pending Order.", sym);
        }
        // 4. Check Recent Related Signals (if single recent signal exists)
        else if (context.RelatedSignals != null && context.RelatedSignals.Count == 1)
        {
            var sym = context.RelatedSignals.First().Symbol;
            resolvedTargets.Add(sym);
            _logger.LogInformation("TargetResolver: Resolved target symbol '{Symbol}' from single Related Signal.", sym);
        }

        if (resolvedTargets.Any())
        {
            intent.Targets = resolvedTargets;
            intent.Scope = IntentScope.SYMBOL;
        }
        else
        {
            _logger.LogWarning("TargetResolver: Target resolution was ambiguous or unresolvable for MessageId {MessageId}. Marking intent as UNKNOWN to prevent unsafe execution.",
                context.CurrentMessage.MessageId);

            intent.RequiresExecution = false;
            intent.Reason += " [Target Resolution Failed: Ambiguous target]";
        }

        return Task.FromResult(intent);
    }
}
