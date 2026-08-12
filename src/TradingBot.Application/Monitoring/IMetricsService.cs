using System;
using System.Collections.Generic;

namespace TradingBot.Application.Monitoring;

public interface IMetricsService
{
    // Alert Metrics
    void IncrementAlertsTriggered();
    void IncrementAlertsResolved();
    void IncrementAlertsDeduplicated();
    void IncrementNotificationsSuppressed();
    void IncrementNotificationsCreated();

    // Notification Metrics
    void IncrementNotificationsDelivered();
    void IncrementNotificationsFailed();
    void IncrementNotificationsRetried();

    // System Metrics
    TimeSpan GetUptime();
    void IncrementSystemErrors();
    void IncrementSystemWarnings();
    void IncrementSystemCriticalErrors();

    // Trading Metrics
    void IncrementSignalsReceived();
    void IncrementSignalsAccepted();
    void IncrementSignalsRejected();
    void IncrementOrdersSubmitted();
    void IncrementOrdersFilled();
    void IncrementOrdersFailed();
    void IncrementOrdersRejected();
    void IncrementOrdersCancelled();
    void IncrementPositionsOpened();
    void IncrementPositionsClosed();

    // Telegram Metrics
    void IncrementTelegramMessagesReceived();
    void IncrementTelegramMessagesProcessed();
    void IncrementTelegramMessagesFailed();

    // Connection Metrics
    void RecordConnectionAttempt(string serviceName);
    void RecordConnectionSuccess(string serviceName);
    void RecordConnectionFailure(string serviceName);
    void RecordConnectionStatus(string serviceName, string status);

    // Worker Metrics
    void RecordWorkerStart(string workerName);
    void RecordWorkerFailure(string workerName, string error);
    void RecordWorkerRestart(string workerName);
    void RecordWorkerHeartbeat(string workerName, string state);

    // API Metrics
    void RecordApiCall(string apiName, double latencyMs, bool success, bool isTimeout, bool isRateLimit);

    // Latency Metrics
    void RecordLatency(string pathName, double latencyMs);

    // Idempotency and Recovery Metrics (Phase 10 Stage 10-03)
    void IncrementDuplicateSignals();
    void IncrementDuplicateEvents();
    void IncrementDuplicateOrdersPrevented();
    void IncrementUnknownOrders();
    void IncrementRecoveredOperations();
    void IncrementUnsafeRetriesBlocked();
    void IncrementManualInterventions();

    // Signal Intelligence Stage 05 Metrics
    void IncrementMessagesProcessed();
    void IncrementParserSuccessCount();
    void IncrementAIUsageCount();
    void IncrementAIFailureCount();
    void IncrementValidationFailureCount();
    void IncrementDuplicateCount();
    void RecordAverageProcessingTime(double latencyMs);

    // Get aggregated metrics dictionary
    Dictionary<string, object> GetAggregatedMetrics();
}
