using System;
using TradingBot.Domain.RiskManagement.Entities;

namespace TradingBot.Application.RiskManagement.Workflow;

public class WorkflowResult
{
    public bool IsSuccess { get; }
    public Guid SignalId { get; }
    public string Message { get; }
    public RiskEvaluation? RiskEvaluation { get; }
    public TradeDecision? TradeDecision { get; }
    public Exception? Exception { get; }

    private WorkflowResult(bool isSuccess, Guid signalId, string message, RiskEvaluation? riskEvaluation, TradeDecision? tradeDecision, Exception? exception)
    {
        IsSuccess = isSuccess;
        SignalId = signalId;
        Message = message ?? string.Empty;
        RiskEvaluation = riskEvaluation;
        TradeDecision = tradeDecision;
        Exception = exception;
    }

    public static WorkflowResult Success(Guid signalId, string message, RiskEvaluation riskEvaluation, TradeDecision tradeDecision)
    {
        return new WorkflowResult(true, signalId, message, riskEvaluation, tradeDecision, null);
    }

    public static WorkflowResult Failure(Guid signalId, string message, Exception? exception = null)
    {
        return new WorkflowResult(false, signalId, message, null, null, exception);
    }
}
