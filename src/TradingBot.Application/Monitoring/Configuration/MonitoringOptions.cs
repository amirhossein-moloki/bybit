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
