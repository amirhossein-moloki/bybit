using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.RiskManagement.Services;

public class RiskAuditService : IRiskAuditService
{
    private readonly ISystemLogRepository _systemLogRepository;
    private readonly ILogger<RiskAuditService> _logger;

    public RiskAuditService(ISystemLogRepository systemLogRepository, ILogger<RiskAuditService> logger)
    {
        _systemLogRepository = systemLogRepository ?? throw new ArgumentNullException(nameof(systemLogRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordEvaluationStartedAsync(Guid signalId, CancellationToken cancellationToken = default)
    {
        var message = "Evaluation Started";
        _logger.LogInformation("Risk Workflow: Signal {SignalId} - {Message}", signalId, message);
        var auditLog = SystemLog.CreateAuditLog("INFO", "RiskEvaluation", "Signal", signalId.ToString(), message);
        await _systemLogRepository.AddAsync(auditLog, cancellationToken);
    }

    public async Task RecordRulesExecutedAsync(Guid signalId, IEnumerable<string> rules, CancellationToken cancellationToken = default)
    {
        var ruleList = string.Join(", ", rules);
        var message = $"Rules Executed: {ruleList}";
        _logger.LogInformation("Risk Workflow: Signal {SignalId} - {Message}", signalId, message);
        var auditLog = SystemLog.CreateAuditLog("INFO", "RiskEvaluationRules", "Signal", signalId.ToString(), message);
        await _systemLogRepository.AddAsync(auditLog, cancellationToken);
    }

    public async Task RecordRuleFailuresAsync(Guid signalId, IEnumerable<string> failures, CancellationToken cancellationToken = default)
    {
        var failureList = string.Join(", ", failures);
        var message = $"Rule Failures: {failureList}";
        _logger.LogWarning("Risk Workflow: Signal {SignalId} - {Message}", signalId, message);
        var auditLog = SystemLog.CreateAuditLog("WARNING", "RiskEvaluationFailures", "Signal", signalId.ToString(), message);
        await _systemLogRepository.AddAsync(auditLog, cancellationToken);
    }

    public async Task RecordFinalDecisionAsync(Guid signalId, RiskDecisionStatus decision, string reason, CancellationToken cancellationToken = default)
    {
        var message = $"Final Decision: {decision} | Reason: {reason}";
        _logger.LogInformation("Risk Workflow: Signal {SignalId} - {Message}", signalId, message);
        var auditLog = SystemLog.CreateAuditLog("INFO", "RiskEvaluationDecision", "Signal", signalId.ToString(), message);
        await _systemLogRepository.AddAsync(auditLog, cancellationToken);
    }

    public async Task RecordProcessingDurationAsync(Guid signalId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var message = $"Processing Duration: {duration.TotalMilliseconds} ms";
        _logger.LogInformation("Risk Workflow: Signal {SignalId} - {Message}", signalId, message);
        var auditLog = SystemLog.CreateAuditLog("INFO", "RiskEvaluationDuration", "Signal", signalId.ToString(), message);
        await _systemLogRepository.AddAsync(auditLog, cancellationToken);
    }
}
