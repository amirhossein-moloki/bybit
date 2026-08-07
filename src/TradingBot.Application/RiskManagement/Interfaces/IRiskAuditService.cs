using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.RiskManagement.Interfaces;

public interface IRiskAuditService
{
    Task RecordEvaluationStartedAsync(Guid signalId, CancellationToken cancellationToken = default);
    Task RecordRulesExecutedAsync(Guid signalId, IEnumerable<string> rules, CancellationToken cancellationToken = default);
    Task RecordRuleFailuresAsync(Guid signalId, IEnumerable<string> failures, CancellationToken cancellationToken = default);
    Task RecordFinalDecisionAsync(Guid signalId, RiskDecisionStatus decision, string reason, CancellationToken cancellationToken = default);
    Task RecordProcessingDurationAsync(Guid signalId, TimeSpan duration, CancellationToken cancellationToken = default);
}
