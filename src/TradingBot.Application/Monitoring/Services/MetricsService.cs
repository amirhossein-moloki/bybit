using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TradingBot.Application.Monitoring.Services;

public class MetricsService : IMetricsService
{
    private readonly DateTime _startTime = DateTime.UtcNow;

    // Counters and Gauges
    private long _alertsTriggered;
    private long _alertsResolved;
    private long _alertsDeduplicated;
    private long _notificationsSuppressed;
    private long _notificationsCreated;
    private long _notificationsDelivered;
    private long _notificationsFailed;
    private long _notificationsRetried;

    private long _systemErrors;
    private long _systemWarnings;
    private long _systemCriticalErrors;

    private long _signalsReceived;
    private long _signalsAccepted;
    private long _signalsRejected;

    private long _ordersSubmitted;
    private long _ordersFilled;
    private long _ordersFailed;
    private long _ordersRejected;
    private long _ordersCancelled;

    private long _positionsOpened;
    private long _positionsClosed;

    private long _telegramMessagesReceived;
    private long _telegramMessagesProcessed;
    private long _telegramMessagesFailed;

    // Idempotency and Recovery Counters
    private long _duplicateSignals;
    private long _duplicateEvents;
    private long _duplicateOrdersPrevented;
    private long _unknownOrders;
    private long _recoveredOperations;
    private long _unsafeRetriesBlocked;
    private long _manualInterventions;

    // Signal Intelligence Stage 05 fields
    private long _messagesProcessed;
    private long _parserSuccessCount;
    private long _aiUsageCount;
    private long _aiFailureCount;
    private long _validationFailureCount;
    private long _duplicateCount;
    private double _totalProcessingTime;
    private long _processingTimeCount;

    // Connection Metrics tracking
    private readonly ConcurrentDictionary<string, ConnectionMetric> _connections = new(StringComparer.OrdinalIgnoreCase);

    // Worker Metrics tracking
    private readonly ConcurrentDictionary<string, WorkerMetric> _workers = new(StringComparer.OrdinalIgnoreCase);

    // API Call Metrics tracking
    private readonly ConcurrentDictionary<string, ApiMetric> _apiMetrics = new(StringComparer.OrdinalIgnoreCase);

    // Latency Paths
    private readonly ConcurrentDictionary<string, LatencyTracker> _latencyPaths = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan GetUptime() => DateTime.UtcNow - _startTime;

    public void IncrementAlertsTriggered() => Interlocked.Increment(ref _alertsTriggered);
    public void IncrementAlertsResolved() => Interlocked.Increment(ref _alertsResolved);
    public void IncrementAlertsDeduplicated() => Interlocked.Increment(ref _alertsDeduplicated);
    public void IncrementNotificationsSuppressed() => Interlocked.Increment(ref _notificationsSuppressed);
    public void IncrementNotificationsCreated() => Interlocked.Increment(ref _notificationsCreated);

    public void IncrementNotificationsDelivered() => Interlocked.Increment(ref _notificationsDelivered);
    public void IncrementNotificationsFailed() => Interlocked.Increment(ref _notificationsFailed);
    public void IncrementNotificationsRetried() => Interlocked.Increment(ref _notificationsRetried);

    public void IncrementSystemErrors() => Interlocked.Increment(ref _systemErrors);
    public void IncrementSystemWarnings() => Interlocked.Increment(ref _systemWarnings);
    public void IncrementSystemCriticalErrors() => Interlocked.Increment(ref _systemCriticalErrors);

    public void IncrementSignalsReceived() => Interlocked.Increment(ref _signalsReceived);
    public void IncrementSignalsAccepted() => Interlocked.Increment(ref _signalsAccepted);
    public void IncrementSignalsRejected() => Interlocked.Increment(ref _signalsRejected);

    public void IncrementOrdersSubmitted() => Interlocked.Increment(ref _ordersSubmitted);
    public void IncrementOrdersFilled() => Interlocked.Increment(ref _ordersFilled);
    public void IncrementOrdersFailed() => Interlocked.Increment(ref _ordersFailed);
    public void IncrementOrdersRejected() => Interlocked.Increment(ref _ordersRejected);
    public void IncrementOrdersCancelled() => Interlocked.Increment(ref _ordersCancelled);

    public void IncrementPositionsOpened() => Interlocked.Increment(ref _positionsOpened);
    public void IncrementPositionsClosed() => Interlocked.Increment(ref _positionsClosed);

    public void IncrementTelegramMessagesReceived() => Interlocked.Increment(ref _telegramMessagesReceived);
    public void IncrementTelegramMessagesProcessed() => Interlocked.Increment(ref _telegramMessagesProcessed);
    public void IncrementTelegramMessagesFailed() => Interlocked.Increment(ref _telegramMessagesFailed);

    // Idempotency and Recovery counter increments
    public void IncrementDuplicateSignals() => Interlocked.Increment(ref _duplicateSignals);
    public void IncrementDuplicateEvents() => Interlocked.Increment(ref _duplicateEvents);
    public void IncrementDuplicateOrdersPrevented() => Interlocked.Increment(ref _duplicateOrdersPrevented);
    public void IncrementUnknownOrders() => Interlocked.Increment(ref _unknownOrders);
    public void IncrementRecoveredOperations() => Interlocked.Increment(ref _recoveredOperations);
    public void IncrementUnsafeRetriesBlocked() => Interlocked.Increment(ref _unsafeRetriesBlocked);
    public void IncrementManualInterventions() => Interlocked.Increment(ref _manualInterventions);

    // Signal Intelligence Stage 05 counter increments
    public void IncrementMessagesProcessed() => Interlocked.Increment(ref _messagesProcessed);
    public void IncrementParserSuccessCount() => Interlocked.Increment(ref _parserSuccessCount);
    public void IncrementAIUsageCount() => Interlocked.Increment(ref _aiUsageCount);
    public void IncrementAIFailureCount() => Interlocked.Increment(ref _aiFailureCount);
    public void IncrementValidationFailureCount() => Interlocked.Increment(ref _validationFailureCount);
    public void IncrementDuplicateCount() => Interlocked.Increment(ref _duplicateCount);
    public void RecordAverageProcessingTime(double latencyMs)
    {
        lock (this)
        {
            _totalProcessingTime += latencyMs;
            _processingTimeCount++;
        }
    }

    public void RecordConnectionAttempt(string serviceName)
    {
        var metric = _connections.GetOrAdd(serviceName, _ => new ConnectionMetric());
        Interlocked.Increment(ref metric.Attempts);
    }

    public void RecordConnectionSuccess(string serviceName)
    {
        var metric = _connections.GetOrAdd(serviceName, _ => new ConnectionMetric());
        metric.Status = "Connected";
        metric.LastSuccessAt = DateTime.UtcNow;
    }

    public void RecordConnectionFailure(string serviceName)
    {
        var metric = _connections.GetOrAdd(serviceName, _ => new ConnectionMetric());
        metric.Status = "Disconnected";
        metric.LastFailureAt = DateTime.UtcNow;
        Interlocked.Increment(ref metric.Failures);
    }

    public void RecordConnectionStatus(string serviceName, string status)
    {
        var metric = _connections.GetOrAdd(serviceName, _ => new ConnectionMetric());
        metric.Status = status;
        if (status.Equals("Connected", StringComparison.OrdinalIgnoreCase) || status.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
        {
            metric.LastSuccessAt = DateTime.UtcNow;
        }
        else
        {
            metric.LastFailureAt = DateTime.UtcNow;
        }
    }

    public void RecordWorkerStart(string workerName)
    {
        var metric = _workers.GetOrAdd(workerName, _ => new WorkerMetric());
        metric.State = "Started";
        metric.LastStartedAt = DateTime.UtcNow;
        Interlocked.Increment(ref metric.Starts);
    }

    public void RecordWorkerFailure(string workerName, string error)
    {
        var metric = _workers.GetOrAdd(workerName, _ => new WorkerMetric());
        metric.State = "Failed";
        metric.LastFailureError = error;
        metric.LastFailureAt = DateTime.UtcNow;
        Interlocked.Increment(ref metric.Failures);
    }

    public void RecordWorkerRestart(string workerName)
    {
        var metric = _workers.GetOrAdd(workerName, _ => new WorkerMetric());
        metric.State = "Restarted";
        metric.LastStartedAt = DateTime.UtcNow;
        Interlocked.Increment(ref metric.Restarts);
    }

    public void RecordWorkerHeartbeat(string workerName, string state)
    {
        var metric = _workers.GetOrAdd(workerName, _ => new WorkerMetric());
        metric.State = state;
        metric.LastHeartbeatAt = DateTime.UtcNow;
    }

    public void RecordApiCall(string apiName, double latencyMs, bool success, bool isTimeout, bool isRateLimit)
    {
        var metric = _apiMetrics.GetOrAdd(apiName, _ => new ApiMetric());
        Interlocked.Increment(ref metric.RequestCount);

        if (success)
        {
            Interlocked.Increment(ref metric.SuccessCount);
        }
        else
        {
            Interlocked.Increment(ref metric.FailureCount);
        }

        if (isTimeout) Interlocked.Increment(ref metric.TimeoutCount);
        if (isRateLimit) Interlocked.Increment(ref metric.RateLimitCount);

        if (latencyMs >= 0)
        {
            metric.RecordLatency(latencyMs);
        }
    }

    public void RecordLatency(string pathName, double latencyMs)
    {
        if (latencyMs < 0) return;
        var tracker = _latencyPaths.GetOrAdd(pathName, _ => new LatencyTracker());
        tracker.Record(latencyMs);
    }

    public Dictionary<string, object> GetAggregatedMetrics()
    {
        var aggregated = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Uptime"] = GetUptime().ToString(),
            ["UptimeSeconds"] = GetUptime().TotalSeconds,

            // Alert Metrics
            ["AlertsTriggered"] = Interlocked.Read(ref _alertsTriggered),
            ["AlertsResolved"] = Interlocked.Read(ref _alertsResolved),
            ["AlertsDeduplicated"] = Interlocked.Read(ref _alertsDeduplicated),
            ["NotificationsSuppressed"] = Interlocked.Read(ref _notificationsSuppressed),
            ["NotificationsCreated"] = Interlocked.Read(ref _notificationsCreated),

            // Notification Metrics
            ["NotificationsDelivered"] = Interlocked.Read(ref _notificationsDelivered),
            ["NotificationsFailed"] = Interlocked.Read(ref _notificationsFailed),
            ["NotificationsRetried"] = Interlocked.Read(ref _notificationsRetried),

            // System Metrics
            ["SystemErrors"] = Interlocked.Read(ref _systemErrors),
            ["SystemWarnings"] = Interlocked.Read(ref _systemWarnings),
            ["SystemCriticalErrors"] = Interlocked.Read(ref _systemCriticalErrors),

            // Trading Metrics
            ["SignalsReceived"] = Interlocked.Read(ref _signalsReceived),
            ["SignalsAccepted"] = Interlocked.Read(ref _signalsAccepted),
            ["SignalsRejected"] = Interlocked.Read(ref _signalsRejected),
            ["OrdersSubmitted"] = Interlocked.Read(ref _ordersSubmitted),
            ["OrdersFilled"] = Interlocked.Read(ref _ordersFilled),
            ["OrdersFailed"] = Interlocked.Read(ref _ordersFailed),
            ["OrdersRejected"] = Interlocked.Read(ref _ordersRejected),
            ["OrdersCancelled"] = Interlocked.Read(ref _ordersCancelled),
            ["PositionsOpened"] = Interlocked.Read(ref _positionsOpened),
            ["PositionsClosed"] = Interlocked.Read(ref _positionsClosed),

            // Telegram Metrics
            ["TelegramMessagesReceived"] = Interlocked.Read(ref _telegramMessagesReceived),
            ["TelegramMessagesProcessed"] = Interlocked.Read(ref _telegramMessagesProcessed),
            ["TelegramMessagesFailed"] = Interlocked.Read(ref _telegramMessagesFailed),

            // Idempotency and Recovery Metrics
            ["DuplicateSignals"] = Interlocked.Read(ref _duplicateSignals),
            ["DuplicateEvents"] = Interlocked.Read(ref _duplicateEvents),
            ["DuplicateOrdersPrevented"] = Interlocked.Read(ref _duplicateOrdersPrevented),
            ["UnknownOrders"] = Interlocked.Read(ref _unknownOrders),
            ["RecoveredOperations"] = Interlocked.Read(ref _recoveredOperations),
            ["UnsafeRetriesBlocked"] = Interlocked.Read(ref _unsafeRetriesBlocked),
            ["ManualInterventions"] = Interlocked.Read(ref _manualInterventions),

            // Signal Intelligence Stage 05 Metrics
            ["MessagesProcessed"] = Interlocked.Read(ref _messagesProcessed),
            ["ParserSuccessCount"] = Interlocked.Read(ref _parserSuccessCount),
            ["AIUsageCount"] = Interlocked.Read(ref _aiUsageCount),
            ["AIFailureCount"] = Interlocked.Read(ref _aiFailureCount),
            ["ValidationFailureCount"] = Interlocked.Read(ref _validationFailureCount),
            ["DuplicateCount"] = Interlocked.Read(ref _duplicateCount),
            ["AverageProcessingTime"] = _processingTimeCount == 0 ? 0 : (_totalProcessingTime / _processingTimeCount)
        };

        // Connections
        var connsDict = new Dictionary<string, object>();
        foreach (var kvp in _connections)
        {
            connsDict[kvp.Key] = new
            {
                kvp.Value.Status,
                kvp.Value.Attempts,
                kvp.Value.Failures,
                LastSuccessAt = kvp.Value.LastSuccessAt?.ToString("o"),
                LastFailureAt = kvp.Value.LastFailureAt?.ToString("o")
            };
        }
        aggregated["Connections"] = connsDict;

        // Workers
        var workersDict = new Dictionary<string, object>();
        foreach (var kvp in _workers)
        {
            workersDict[kvp.Key] = new
            {
                kvp.Value.State,
                kvp.Value.Starts,
                kvp.Value.Failures,
                kvp.Value.Restarts,
                LastHeartbeatAt = kvp.Value.LastHeartbeatAt?.ToString("o"),
                LastFailureError = kvp.Value.LastFailureError,
                LastFailureAt = kvp.Value.LastFailureAt?.ToString("o")
            };
        }
        aggregated["Workers"] = workersDict;

        // APIs
        var apisDict = new Dictionary<string, object>();
        foreach (var kvp in _apiMetrics)
        {
            apisDict[kvp.Key] = new
            {
                kvp.Value.RequestCount,
                kvp.Value.SuccessCount,
                kvp.Value.FailureCount,
                kvp.Value.TimeoutCount,
                kvp.Value.RateLimitCount,
                AvgLatencyMs = kvp.Value.GetAverageLatency(),
                MinLatencyMs = kvp.Value.MinLatency == double.MaxValue ? 0 : kvp.Value.MinLatency,
                MaxLatencyMs = kvp.Value.MaxLatency == double.MinValue ? 0 : kvp.Value.MaxLatency
            };
        }
        aggregated["ApiMetrics"] = apisDict;

        // Latency paths
        var pathsDict = new Dictionary<string, object>();
        foreach (var kvp in _latencyPaths)
        {
            pathsDict[kvp.Key] = new
            {
                AvgLatencyMs = kvp.Value.GetAverage(),
                MinLatencyMs = kvp.Value.Min == double.MaxValue ? 0 : kvp.Value.Min,
                MaxLatencyMs = kvp.Value.Max == double.MinValue ? 0 : kvp.Value.Max,
                Count = kvp.Value.Count
            };
        }
        aggregated["LatencyPaths"] = pathsDict;

        return aggregated;
    }

    private class ConnectionMetric
    {
        public string Status = "Unknown";
        public long Attempts;
        public long Failures;
        public DateTime? LastSuccessAt;
        public DateTime? LastFailureAt;
    }

    private class WorkerMetric
    {
        public string State = "Unknown";
        public long Starts;
        public long Failures;
        public long Restarts;
        public DateTime? LastStartedAt;
        public DateTime? LastHeartbeatAt;
        public DateTime? LastFailureAt;
        public string? LastFailureError;
    }

    private class ApiMetric
    {
        public long RequestCount;
        public long SuccessCount;
        public long FailureCount;
        public long TimeoutCount;
        public long RateLimitCount;

        private double _totalLatency;
        public double MinLatency { get; private set; } = double.MaxValue;
        public double MaxLatency { get; private set; } = double.MinValue;
        private long _latencyCount;

        public void RecordLatency(double latencyMs)
        {
            lock (this)
            {
                _totalLatency += latencyMs;
                _latencyCount++;
                if (latencyMs < MinLatency) MinLatency = latencyMs;
                if (latencyMs > MaxLatency) MaxLatency = latencyMs;
            }
        }

        public double GetAverageLatency()
        {
            var count = Interlocked.Read(ref _latencyCount);
            if (count == 0) return 0;
            lock (this)
            {
                return _totalLatency / count;
            }
        }
    }

    private class LatencyTracker
    {
        private double _total;
        public double Min { get; private set; } = double.MaxValue;
        public double Max { get; private set; } = double.MinValue;
        public long Count { get; private set; }

        public void Record(double val)
        {
            lock (this)
            {
                _total += val;
                Count++;
                if (val < Min) Min = val;
                if (val > Max) Max = val;
            }
        }

        public double GetAverage()
        {
            lock (this)
            {
                return Count == 0 ? 0 : _total / Count;
            }
        }
    }
}
