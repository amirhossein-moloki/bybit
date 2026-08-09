using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Queries;

public class SystemHealthQueryService : ISystemHealthQueryService
{
    private readonly TradingDbContext _dbContext;
    private readonly IHealthStatusProvider? _healthStatusProvider;
    private readonly IMetricsService? _metricsService;
    private readonly IWorkerHealthRegistry? _workerHealthRegistry;
    private readonly IEventSanitizer _eventSanitizer;

    public SystemHealthQueryService(
        TradingDbContext dbContext,
        IHealthStatusProvider? healthStatusProvider = null,
        IMetricsService? metricsService = null,
        IWorkerHealthRegistry? workerHealthRegistry = null,
        IEventSanitizer? eventSanitizer = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _healthStatusProvider = healthStatusProvider;
        _metricsService = metricsService;
        _workerHealthRegistry = workerHealthRegistry;
        _eventSanitizer = eventSanitizer ?? new EventSanitizer();
    }

    public async Task<SystemHealthOverviewDto> GetOverviewAsync(
        int recentAlertsLimit = 20,
        int recentEventsLimit = 20,
        int healthHistoryLimit = 20,
        CancellationToken cancellationToken = default)
    {
        // Enforce limits and boundaries (Section 24)
        if (recentAlertsLimit <= 0) recentAlertsLimit = 20;
        if (recentAlertsLimit > 100) recentAlertsLimit = 100;

        if (recentEventsLimit <= 0) recentEventsLimit = 20;
        if (recentEventsLimit > 100) recentEventsLimit = 100;

        if (healthHistoryLimit <= 0) healthHistoryLimit = 20;
        if (healthHistoryLimit > 100) healthHistoryLimit = 100;

        // 1. Database Section (Section 8)
        var dbCheck = _healthStatusProvider?.GetComponentStatus("Database");
        if (dbCheck == null)
        {
            dbCheck = await _dbContext.HealthCheckResults
                .AsNoTracking()
                .Where(r => r.ServiceName == "Database")
                .OrderByDescending(r => r.CheckedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var databaseDto = new DatabaseHealthDto(
            dbCheck?.Status.ToString() ?? "Unknown",
            dbCheck?.CheckedAt,
            dbCheck?.DurationMs
        );

        // 2. Bybit Section (Section 9)
        var restCheck = _healthStatusProvider?.GetComponentStatus("Bybit REST") ??
                        _healthStatusProvider?.GetComponentStatus("BybitRest");
        if (restCheck == null)
        {
            restCheck = await _dbContext.HealthCheckResults
                .AsNoTracking()
                .Where(r => r.ServiceName == "Bybit REST" || r.ServiceName == "BybitRest")
                .OrderByDescending(r => r.CheckedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var restDto = new BybitServiceStatusDto(
            restCheck?.Status.ToString() ?? "Unknown",
            restCheck?.CheckedAt,
            restCheck?.DurationMs
        );

        var wsCheck = _healthStatusProvider?.GetComponentStatus("Bybit WebSocket") ??
                      _healthStatusProvider?.GetComponentStatus("BybitWebSocket");
        if (wsCheck == null)
        {
            wsCheck = await _dbContext.HealthCheckResults
                .AsNoTracking()
                .Where(r => r.ServiceName == "Bybit WebSocket" || r.ServiceName == "BybitWebSocket")
                .OrderByDescending(r => r.CheckedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var wsDto = new BybitWebSocketStatusDto(
            wsCheck?.Status.ToString() ?? "Unknown",
            null,
            null,
            null,
            null
        );

        var bybitDto = new BybitHealthDto(
            restDto,
            wsDto,
            GetAuthenticationStatus(restCheck)
        );

        // 3. Telegram Section (Section 10)
        var tgCheck = _healthStatusProvider?.GetComponentStatus("Telegram");
        if (tgCheck == null)
        {
            tgCheck = await _dbContext.HealthCheckResults
                .AsNoTracking()
                .Where(r => r.ServiceName == "Telegram")
                .OrderByDescending(r => r.CheckedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var telegramDto = new TelegramHealthDto(
            tgCheck?.Status.ToString() ?? "Unknown",
            tgCheck?.CheckedAt,
            null,
            null
        );

        // 4. Workers Section (Section 11 & 12)
        var workerStatuses = new List<WorkerStatusDto>();
        var heartbeats = _workerHealthRegistry?.GetWorkerHeartbeats();
        if (heartbeats != null && heartbeats.Count > 0)
        {
            foreach (var hb in heartbeats.Values)
            {
                workerStatuses.Add(new WorkerStatusDto(
                    hb.WorkerName,
                    hb.Status,
                    hb.LastHeartbeatAt,
                    hb.Status == "Running" || hb.Status == "Started" ? hb.LastHeartbeatAt : null,
                    hb.LastErrorAt
                ));
            }
        }
        else
        {
            // Fallback: parse from Workers health check metadata in DB
            var workersCheck = await _dbContext.HealthCheckResults
                .AsNoTracking()
                .Where(r => r.ServiceName == "Workers")
                .OrderByDescending(r => r.CheckedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (workersCheck != null && !string.IsNullOrEmpty(workersCheck.Metadata))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(workersCheck.Metadata);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var name = prop.Name;
                        var status = prop.Value.GetProperty("Status").GetString() ?? "Unknown";
                        workerStatuses.Add(new WorkerStatusDto(
                            name,
                            status,
                            workersCheck.CheckedAt,
                            status == "Running" || status == "Started" ? workersCheck.CheckedAt : null,
                            null
                        ));
                    }
                }
                catch { }
            }
        }

        // 5. Monitoring Status Section (Section 13)
        var latestCheck = await _dbContext.HealthCheckResults
            .AsNoTracking()
            .OrderByDescending(r => r.CheckedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var lastFailureCheck = await _dbContext.HealthCheckResults
            .AsNoTracking()
            .Where(r => r.Status == HealthStatus.Unhealthy)
            .OrderByDescending(r => r.CheckedAt)
            .FirstOrDefaultAsync(cancellationToken);

        string monitoringStatus = "Offline";
        if (latestCheck != null)
        {
            monitoringStatus = (DateTime.UtcNow - latestCheck.CheckedAt) <= TimeSpan.FromSeconds(30)
                ? "Operational"
                : "Stale";
        }

        var monitoringDto = new MonitoringStatusDto(
            monitoringStatus,
            latestCheck?.CheckedAt,
            lastFailureCheck?.CheckedAt
        );

        // 6. Active Alerts Section (Section 14, 15, 16)
        var activeAlertsList = await _dbContext.Alerts
            .AsNoTracking()
            .Where(a => a.Status != "Resolved")
            .ToListAsync(cancellationToken);

        var activeAlertCount = activeAlertsList.Count;
        var criticalAlertCount = activeAlertsList.Count(a => a.Severity.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase));
        var errorAlertCount = activeAlertsList.Count(a => a.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase));
        var warningAlertCount = activeAlertsList.Count(a => a.Severity.Equals("WARNING", StringComparison.OrdinalIgnoreCase));
        var infoAlertCount = activeAlertsList.Count(a => a.Severity.Equals("INFORMATION", StringComparison.OrdinalIgnoreCase) ||
                                                          a.Severity.Equals("INFO", StringComparison.OrdinalIgnoreCase));

        var alertSummaryDto = new AlertSummaryDto(
            activeAlertCount,
            criticalAlertCount,
            errorAlertCount,
            warningAlertCount,
            infoAlertCount
        );

        var sortedActiveAlerts = activeAlertsList
            .OrderByDescending(a => GetSeverityRank(a.Severity))
            .ThenByDescending(a => a.TriggeredAt)
            .Take(recentAlertsLimit)
            .Select(a => new AlertDto(
                a.Id,
                a.Type,
                a.Severity,
                a.Source,
                a.Status,
                _eventSanitizer.Sanitize(a.Message) ?? "",
                a.TriggeredAt,
                a.UpdatedAt,
                a.CorrelationId
            ))
            .ToList();

        // 7. Recent System Events (Section 17 & 18) - Project first, materialize, then map and sanitize in memory
        var recentEventsRaw = await _dbContext.MonitoringEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .Take(recentEventsLimit)
            .Select(e => new {
                e.Id,
                e.EventType,
                e.Severity,
                e.Source,
                e.Timestamp,
                e.CorrelationId,
                e.Message
            })
            .ToListAsync(cancellationToken);

        var recentEvents = recentEventsRaw
            .Select(e => new RecentEventDto(
                e.Id,
                e.EventType,
                e.Severity,
                e.Source,
                e.Timestamp,
                e.CorrelationId,
                _eventSanitizer.Sanitize(e.Message) ?? ""
            ))
            .ToList();

        // 8. Health History (Section 19) - Project first, materialize, then map in memory
        var healthHistoryRaw = await _dbContext.HealthCheckResults
            .AsNoTracking()
            .OrderByDescending(h => h.CheckedAt)
            .Take(healthHistoryLimit)
            .Select(h => new {
                h.ServiceName,
                h.Status,
                h.CheckedAt,
                h.DurationMs
            })
            .ToListAsync(cancellationToken);

        var healthHistory = healthHistoryRaw
            .Select(h => new HealthHistoryRecordDto(
                h.ServiceName,
                h.Status.ToString(),
                h.CheckedAt,
                h.DurationMs
            ))
            .ToList();

        // 9. Operational Metrics (Section 20 & 21)
        var metricsDto = GetOperationalMetrics();

        // 10. Application Status & Overall Status (Section 7, 22, 23)
        var overallStatus = GetOverallStatus(databaseDto, bybitDto, telegramDto, workerStatuses);

        var appUptime = _metricsService?.GetUptime() ?? TimeSpan.Zero;
        var appStartedAt = DateTime.UtcNow - appUptime;

        var applicationDto = new ApplicationStatusDto(
            overallStatus,
            appUptime.ToString(),
            appStartedAt,
            DateTime.UtcNow,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
        );

        return new SystemHealthOverviewDto(
            overallStatus,
            applicationDto,
            databaseDto,
            bybitDto,
            telegramDto,
            workerStatuses,
            monitoringDto,
            alertSummaryDto,
            sortedActiveAlerts,
            recentEvents,
            healthHistory,
            metricsDto
        );
    }

    private string GetAuthenticationStatus(HealthCheckResult? restCheck)
    {
        if (restCheck == null) return "Unknown";
        if (restCheck.Status == HealthStatus.Healthy) return "Healthy";
        if (restCheck.ErrorCode == "AuthenticationFailure" || restCheck.ErrorCode == "AUTHENTICATION_FAILED")
            return "Unhealthy";
        if (!string.IsNullOrEmpty(restCheck.Metadata) && restCheck.Metadata.Contains("\"Authenticated\":true"))
            return "Healthy";
        return "Unknown";
    }

    private int GetSeverityRank(string severity)
    {
        if (string.IsNullOrEmpty(severity)) return 0;
        var norm = severity.Trim().ToUpperInvariant();
        return norm switch
        {
            "CRITICAL" => 4,
            "ERROR" => 3,
            "WARNING" => 2,
            "INFORMATION" or "INFO" => 1,
            _ => 0
        };
    }

    private string GetOverallStatus(
        DatabaseHealthDto db,
        BybitHealthDto bybit,
        TelegramHealthDto telegram,
        IReadOnlyList<WorkerStatusDto> workers)
    {
        // 1. Use the IHealthStatusProvider if it has data
        if (_healthStatusProvider != null)
        {
            var status = _healthStatusProvider.GetOverallStatus();
            if (status != HealthStatus.Unknown)
            {
                return status.ToString();
            }
        }

        // 2. Fallback deterministic aggregation (Section 22 & 23)
        var statuses = new List<string> { db.Status, bybit.Rest.Status, bybit.WebSocket.Status, telegram.Status };
        foreach (var w in workers)
        {
            statuses.Add(w.Status);
        }

        // Critical services: Database, Bybit REST, Bybit WebSocket
        bool isDbUnhealthy = db.Status == "Unhealthy" || db.Status == "Failed" || db.Status == "Critical";
        bool isBybitRestUnhealthy = bybit.Rest.Status == "Unhealthy" || bybit.Rest.Status == "Failed" || bybit.Rest.Status == "Critical";
        bool isBybitWsUnhealthy = bybit.WebSocket.Status == "Unhealthy" || bybit.WebSocket.Status == "Failed" || bybit.WebSocket.Status == "Critical";

        if (isDbUnhealthy || isBybitRestUnhealthy || isBybitWsUnhealthy)
        {
            return "Unhealthy";
        }

        bool hasUnhealthy = false;
        bool hasDegraded = false;
        bool hasUnknown = false;

        foreach (var s in statuses)
        {
            var norm = s.Trim().ToLowerInvariant();
            if (norm == "unhealthy" || norm == "failed" || norm == "critical")
            {
                hasUnhealthy = true;
            }
            else if (norm == "degraded" || norm == "warning")
            {
                hasDegraded = true;
            }
            else if (norm == "unknown")
            {
                hasUnknown = true;
            }
        }

        if (hasUnhealthy) return "Unhealthy";
        if (hasDegraded) return "Degraded";
        if (hasUnknown) return "Unknown";
        return "Healthy";
    }

    private OperationalMetricsDto GetOperationalMetrics()
    {
        if (_metricsService == null)
        {
            return new OperationalMetricsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var dict = _metricsService.GetAggregatedMetrics();
        if (dict == null)
        {
            return new OperationalMetricsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        long GetLong(string key)
        {
            if (dict.TryGetValue(key, out var val))
            {
                if (val is long l) return l;
                if (val is int i) return i;
                try { return Convert.ToInt64(val); } catch { }
            }
            return 0;
        }

        long apiRequestsCount = 0;
        if (dict.TryGetValue("ApiMetrics", out var apisObj) && apisObj is IDictionary apisDict)
        {
            foreach (var val in apisDict.Values)
            {
                if (val != null)
                {
                    var prop = val.GetType().GetProperty("RequestCount");
                    if (prop != null)
                    {
                        apiRequestsCount += Convert.ToInt64(prop.GetValue(val));
                    }
                }
            }
        }

        return new OperationalMetricsDto(
            OrdersSubmitted: GetLong("OrdersSubmitted"),
            OrdersFilled: GetLong("OrdersFilled"),
            OrdersFailed: GetLong("OrdersFailed"),
            MessagesReceived: GetLong("TelegramMessagesReceived"),
            MessagesProcessed: GetLong("TelegramMessagesProcessed"),
            MessagesFailed: GetLong("TelegramMessagesFailed"),
            NotificationsSent: GetLong("NotificationsCreated"),
            NotificationsFailed: GetLong("NotificationsFailed"),
            ErrorCount: GetLong("SystemErrors") + GetLong("SystemCriticalErrors"),
            WarningCount: GetLong("SystemWarnings"),
            ApiRequestsCount: apiRequestsCount
        );
    }

    public async Task<PagedResult<AlertDto>> GetAlertsAsync(
        string? severity = null,
        string? source = null,
        string? type = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _dbContext.Alerts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(severity))
        {
            var sev = severity.Trim();
            query = query.Where(a => a.Severity == sev);
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            var src = source.Trim();
            query = query.Where(a => a.Source == src);
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim();
            query = query.Where(a => a.Type == t);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rawItems = await query
            .OrderByDescending(a => a.TriggeredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new {
                a.Id,
                a.Type,
                a.Severity,
                a.Source,
                a.Status,
                a.Message,
                a.TriggeredAt,
                a.UpdatedAt,
                a.CorrelationId
            })
            .ToListAsync(cancellationToken);

        var items = rawItems
            .Select(a => new AlertDto(
                a.Id,
                a.Type,
                a.Severity,
                a.Source,
                a.Status,
                _eventSanitizer.Sanitize(a.Message) ?? "",
                a.TriggeredAt,
                a.UpdatedAt,
                a.CorrelationId
            ))
            .ToList();

        return new PagedResult<AlertDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<RecentEventDto>> GetEventsAsync(
        string? type = null,
        string? severity = null,
        string? source = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _dbContext.MonitoringEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim();
            query = query.Where(e => e.EventType == t);
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var sev = severity.Trim();
            query = query.Where(e => e.Severity == sev);
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            var src = source.Trim();
            query = query.Where(e => e.Source == src);
        }
        if (from.HasValue)
        {
            query = query.Where(e => e.Timestamp >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(e => e.Timestamp <= to.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rawItems = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new {
                e.Id,
                e.EventType,
                e.Severity,
                e.Source,
                e.Timestamp,
                e.CorrelationId,
                e.Message
            })
            .ToListAsync(cancellationToken);

        var items = rawItems
            .Select(e => new RecentEventDto(
                e.Id,
                e.EventType,
                e.Severity,
                e.Source,
                e.Timestamp,
                e.CorrelationId,
                _eventSanitizer.Sanitize(e.Message) ?? ""
            ))
            .ToList();

        return new PagedResult<RecentEventDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<HealthHistoryRecordDto>> GetHealthHistoryAsync(
        string? service = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _dbContext.HealthCheckResults.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(service))
        {
            var s = service.Trim();
            query = query.Where(h => h.ServiceName == s);
        }
        if (from.HasValue)
        {
            query = query.Where(h => h.CheckedAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(h => h.CheckedAt <= to.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rawItems = await query
            .OrderByDescending(h => h.CheckedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new {
                h.ServiceName,
                h.Status,
                h.CheckedAt,
                h.DurationMs
            })
            .ToListAsync(cancellationToken);

        var items = rawItems
            .Select(h => new HealthHistoryRecordDto(
                h.ServiceName,
                h.Status.ToString(),
                h.CheckedAt,
                h.DurationMs
            ))
            .ToList();

        return new PagedResult<HealthHistoryRecordDto>(items, totalCount, page, pageSize);
    }
}
