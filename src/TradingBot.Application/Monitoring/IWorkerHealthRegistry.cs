using System;
using System.Collections.Generic;

namespace TradingBot.Application.Monitoring;

public interface IWorkerHealthRegistry
{
    void RegisterWorker(string workerName, bool isCritical = true);
    void RecordHeartbeat(string workerName, string status, string? errorMessage = null);
    IReadOnlyDictionary<string, WorkerHeartbeat> GetWorkerHeartbeats();
}

public class WorkerHeartbeat
{
    public string WorkerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // e.g. "Started", "Running", "Stopping", "Stopped", "Failed"
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public bool IsCritical { get; set; }
}
