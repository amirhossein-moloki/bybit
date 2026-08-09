using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TradingBot.Application.Monitoring;

public class WorkerHealthRegistry : IWorkerHealthRegistry
{
    private readonly ConcurrentDictionary<string, WorkerHeartbeat> _heartbeats = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterWorker(string workerName, bool isCritical = true)
    {
        var now = DateTime.UtcNow;
        _heartbeats.GetOrAdd(workerName, name => new WorkerHeartbeat
        {
            WorkerName = name,
            Status = "Started",
            StartedAt = now,
            LastHeartbeatAt = now,
            IsCritical = isCritical
        });
    }

    public void RecordHeartbeat(string workerName, string status, string? errorMessage = null)
    {
        var now = DateTime.UtcNow;
        _heartbeats.AddOrUpdate(workerName,
            name => new WorkerHeartbeat
            {
                WorkerName = name,
                Status = status,
                StartedAt = now,
                LastHeartbeatAt = now,
                IsCritical = true,
                LastErrorMessage = errorMessage,
                LastErrorAt = errorMessage != null ? now : null
            },
            (name, existing) =>
            {
                existing.Status = status;
                existing.LastHeartbeatAt = now;
                if (errorMessage != null)
                {
                    existing.LastErrorMessage = errorMessage;
                    existing.LastErrorAt = now;
                }
                return existing;
            });
    }

    public IReadOnlyDictionary<string, WorkerHeartbeat> GetWorkerHeartbeats()
    {
        return _heartbeats;
    }
}
