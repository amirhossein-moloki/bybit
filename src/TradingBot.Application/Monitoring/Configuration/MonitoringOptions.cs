using System;

namespace TradingBot.Application.Monitoring.Configuration;

public class MonitoringOptions
{
    public bool Enabled { get; set; } = true;
    public HealthCheckSettings Database { get; set; } = new() { Enabled = true, IntervalSeconds = 30, TimeoutSeconds = 5 };
    public HealthCheckSettings BybitRest { get; set; } = new() { Enabled = true, IntervalSeconds = 30, TimeoutSeconds = 5 };
    public HealthCheckSettings BybitWebSocket { get; set; } = new() { Enabled = true, IntervalSeconds = 30 };
    public HealthCheckSettings Telegram { get; set; } = new() { Enabled = true, IntervalSeconds = 30, TimeoutSeconds = 5 };
    public WorkerSettings Workers { get; set; } = new() { Enabled = true, IntervalSeconds = 10, StaleThresholdSeconds = 30 };
    public ObservabilityOptions Observability { get; set; } = new();

    public void Validate()
    {
        if (!Enabled) return;

        if (Database.Enabled)
        {
            if (Database.IntervalSeconds <= 0) throw new ArgumentException("Database health check interval must be positive.");
            if (Database.TimeoutSeconds <= 0) throw new ArgumentException("Database health check timeout must be positive.");
        }
        if (BybitRest.Enabled)
        {
            if (BybitRest.IntervalSeconds <= 0) throw new ArgumentException("Bybit REST health check interval must be positive.");
            if (BybitRest.TimeoutSeconds <= 0) throw new ArgumentException("Bybit REST health check timeout must be positive.");
        }
        if (BybitWebSocket.Enabled)
        {
            if (BybitWebSocket.IntervalSeconds <= 0) throw new ArgumentException("Bybit WebSocket health check interval must be positive.");
        }
        if (Telegram.Enabled)
        {
            if (Telegram.IntervalSeconds <= 0) throw new ArgumentException("Telegram health check interval must be positive.");
            if (Telegram.TimeoutSeconds <= 0) throw new ArgumentException("Telegram health check timeout must be positive.");
        }
        if (Workers.Enabled)
        {
            if (Workers.IntervalSeconds <= 0) throw new ArgumentException("Workers health check interval must be positive.");
            if (Workers.StaleThresholdSeconds <= 0) throw new ArgumentException("Workers stale threshold must be positive.");
        }
        if (Observability.Enabled)
        {
            if (Observability.MaxPayloadSize <= 0) throw new ArgumentException("Max payload size must be positive.");
        }
    }
}

public class HealthCheckSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 5;
}

public class WorkerSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 10;
    public int StaleThresholdSeconds { get; set; } = 30;
}

public class ObservabilityOptions
{
    public bool Enabled { get; set; } = true;
    public bool PersistenceEnabled { get; set; } = true;
    public int MaxPayloadSize { get; set; } = 4096;
    public bool StructuredLogging { get; set; } = true;
    public int MaxPageSize { get; set; } = 100;
}
