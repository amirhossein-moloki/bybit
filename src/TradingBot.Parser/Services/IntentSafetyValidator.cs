using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Services;

public class IntentSafetyValidator : IIntentSafetyValidator
{
    private readonly MessageIntelligenceOptions _options;
    private readonly ILogger<IntentSafetyValidator> _logger;

    public IntentSafetyValidator(
        IOptions<MessageIntelligenceOptions> options,
        ILogger<IntentSafetyValidator> logger)
    {
        _options = options?.Value ?? new MessageIntelligenceOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<(bool IsSafe, string Reason)> ValidateSafetyAsync(
        StructuredIntent intent,
        MessageIntelligenceContext context,
        CancellationToken cancellationToken = default)
    {
        if (intent == null) throw new ArgumentNullException(nameof(intent));
        if (context == null) throw new ArgumentNullException(nameof(context));

        if (!intent.RequiresExecution)
        {
            return Task.FromResult((false, "Intent does not require execution."));
        }

        // 1. Resolution Status Check
        if (intent.ResolutionStatus != ResolutionStatus.Resolved)
        {
            _logger.LogWarning("IntentSafetyValidator: Intent ResolutionStatus is {Status} (not Resolved). Reason: {Reason}",
                intent.ResolutionStatus, intent.Reason);
            return Task.FromResult((false, $"Execution blocked: Intent resolution status is '{intent.ResolutionStatus}'."));
        }

        // 2. Execution Mode Check (Conditional commands MUST NOT be executed immediately)
        if (intent.ExecutionMode != ExecutionMode.Immediate)
        {
            _logger.LogWarning("IntentSafetyValidator: Intent ExecutionMode is {Mode} (not Immediate). Execution deferred/blocked. Reason: {Reason}",
                intent.ExecutionMode, intent.Reason);
            return Task.FromResult((false, $"Execution blocked: Execution mode is '{intent.ExecutionMode}'. Conditional/informational instructions cannot be executed immediately."));
        }

        // 3. Confidence Threshold Check
        if (intent.Confidence < _options.MinimumConfidence)
        {
            _logger.LogWarning("IntentSafetyValidator: Intent confidence {Confidence} is below required threshold {Threshold}. Reason: {Reason}",
                intent.Confidence, _options.MinimumConfidence, intent.Reason);
            return Task.FromResult((false, $"Confidence {intent.Confidence} below required minimum {_options.MinimumConfidence}."));
        }

        // 4. Critical Trading Command Validation against actual Domain Context
        switch (intent.Intent)
        {
            case TradingIntent.RISK_FREE:
                if (intent.Scope == IntentScope.SYMBOL && intent.Targets.Any())
                {
                    var hasPosition = context.ActivePositions.Any(p => intent.Targets.Contains(p.Symbol, StringComparer.OrdinalIgnoreCase));
                    if (!hasPosition)
                    {
                        return Task.FromResult((false, $"No open active position found for target symbol(s): {string.Join(",", intent.Targets)}."));
                    }
                }
                break;

            case TradingIntent.CLOSE:
                if (intent.Scope == IntentScope.SYMBOL && intent.Targets.Any())
                {
                    var hasPosition = context.ActivePositions.Any(p => intent.Targets.Contains(p.Symbol, StringComparer.OrdinalIgnoreCase));
                    if (!hasPosition)
                    {
                        return Task.FromResult((false, $"No open active position found to close for symbol(s): {string.Join(",", intent.Targets)}."));
                    }
                }
                break;

            case TradingIntent.CANCEL_PENDING_ORDERS:
                if (intent.Scope == IntentScope.SYMBOL && intent.Targets.Any())
                {
                    var hasOrder = context.PendingOrders.Any(o => intent.Targets.Contains(o.Symbol, StringComparer.OrdinalIgnoreCase));
                    if (!hasOrder)
                    {
                        return Task.FromResult((false, $"No active pending order found to cancel for symbol(s): {string.Join(",", intent.Targets)}."));
                    }
                }
                else if (intent.Scope == IntentScope.ALL_PENDING_ORDERS)
                {
                    if (context.PendingOrders == null || !context.PendingOrders.Any())
                    {
                        return Task.FromResult((false, "No active pending orders exist to cancel."));
                    }
                }
                break;

            case TradingIntent.CLOSE_ALL:
            case TradingIntent.RISK_FREE_ALL:
                if (context.ActivePositions == null || !context.ActivePositions.Any())
                {
                    return Task.FromResult((false, $"No active open positions exist to execute {intent.Intent}."));
                }
                break;
        }

        return Task.FromResult((true, "Safety and business validations passed successfully."));
    }
}
