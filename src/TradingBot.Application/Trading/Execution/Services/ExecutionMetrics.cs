using System;
using System.Threading;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class ExecutionMetrics : IExecutionMetrics
{
    private long _totalExecutions;
    private long _successfulExecutions;
    private long _failedExecutions;
    private long _rejectedOrders;
    private long _filledOrders;
    private double _totalExecutionTime;

    private double _apiLatency;
    private long _apiErrors;
    private long _rateLimitHits;
    private long _timeoutCount;

    private double _orderPersistenceTime;
    private double _reconciliationDuration;
    private long _failedTransactions;

    public long TotalExecutions => Interlocked.Read(ref _totalExecutions);
    public long SuccessfulExecutions => Interlocked.Read(ref _successfulExecutions);
    public long FailedExecutions => Interlocked.Read(ref _failedExecutions);
    public long RejectedOrders => Interlocked.Read(ref _rejectedOrders);
    public long FilledOrders => Interlocked.Read(ref _filledOrders);
    public double AverageExecutionTime
    {
        get
        {
            long total = TotalExecutions;
            return total == 0 ? 0 : Volatile.Read(ref _totalExecutionTime) / total;
        }
    }

    public double ApiLatency => Volatile.Read(ref _apiLatency);
    public long ApiErrors => Interlocked.Read(ref _apiErrors);
    public long RateLimitHits => Interlocked.Read(ref _rateLimitHits);
    public long TimeoutCount => Interlocked.Read(ref _timeoutCount);

    public double OrderPersistenceTime => Volatile.Read(ref _orderPersistenceTime);
    public double ReconciliationDuration => Volatile.Read(ref _reconciliationDuration);
    public long FailedTransactions => Interlocked.Read(ref _failedTransactions);

    public void RecordExecution(bool success, double durationMs)
    {
        if (durationMs > 0)
        {
            Interlocked.Increment(ref _totalExecutions);
            if (success)
            {
                Interlocked.Increment(ref _successfulExecutions);
            }
            else
            {
                Interlocked.Increment(ref _failedExecutions);
            }

            lock (this)
            {
                _totalExecutionTime += durationMs;
            }
        }
        else
        {
            Interlocked.Increment(ref _totalExecutions);
        }
    }

    public void RecordOrderStatus(OrderStatus status)
    {
        if (status == OrderStatus.Filled)
        {
            Interlocked.Increment(ref _filledOrders);
        }
        else if (status == OrderStatus.Rejected)
        {
            Interlocked.Increment(ref _rejectedOrders);
        }
        else if (status == OrderStatus.Failed)
        {
            Interlocked.Increment(ref _failedExecutions);
        }
    }

    public void RecordExchangeCall(double latencyMs, bool isError, bool isRateLimit, bool isTimeout)
    {
        Volatile.Write(ref _apiLatency, latencyMs);
        if (isError) Interlocked.Increment(ref _apiErrors);
        if (isRateLimit) Interlocked.Increment(ref _rateLimitHits);
        if (isTimeout) Interlocked.Increment(ref _timeoutCount);
    }

    public void RecordDatabasePersistence(double durationMs, bool success)
    {
        Volatile.Write(ref _orderPersistenceTime, durationMs);
        if (!success) Interlocked.Increment(ref _failedTransactions);
    }

    public void RecordReconciliation(double durationMs)
    {
        Volatile.Write(ref _reconciliationDuration, durationMs);
    }
}
