using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IExecutionMetrics
{
    // Execution Metrics
    long TotalExecutions { get; }
    long SuccessfulExecutions { get; }
    long FailedExecutions { get; }
    long RejectedOrders { get; }
    long FilledOrders { get; }
    double AverageExecutionTime { get; }

    // Exchange Metrics
    double ApiLatency { get; }
    long ApiErrors { get; }
    long RateLimitHits { get; }
    long TimeoutCount { get; }

    // Database Metrics
    double OrderPersistenceTime { get; }
    double ReconciliationDuration { get; }
    long FailedTransactions { get; }

    void RecordExecution(bool success, double durationMs);
    void RecordOrderStatus(OrderStatus status);
    void RecordExchangeCall(double latencyMs, bool isError, bool isRateLimit, bool isTimeout);
    void RecordDatabasePersistence(double durationMs, bool success);
    void RecordReconciliation(double durationMs);
}
