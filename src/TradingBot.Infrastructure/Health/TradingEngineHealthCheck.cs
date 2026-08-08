using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Services;

namespace TradingBot.Infrastructure.Health;

public class TradingEngineHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public TradingEngineHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Verify DI Service Registration and availability of Execution Coordinator (Orchestrator)
            using (var scope = _serviceProvider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetService<ITradeExecutionOrchestrator>();
                if (orchestrator == null)
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy("Execution Engine Dependencies are missing: ITradeExecutionOrchestrator is not registered."));
                }

                var metrics = scope.ServiceProvider.GetService<IExecutionMetrics>();
                if (metrics == null)
                {
                    return Task.FromResult(HealthCheckResult.Degraded("IExecutionMetrics is not registered, observability may be impaired."));
                }
            }

            // 2. Verify Background Reconciliation Worker Activity
            var lastReconciled = OrderReconciliationService.LastRunTime;
            if (lastReconciled == DateTime.MinValue)
            {
                return Task.FromResult(HealthCheckResult.Degraded("Trading Engine is active, but reconciliation background pass hasn't run yet."));
            }

            var silenceDuration = DateTime.UtcNow - lastReconciled;
            if (silenceDuration > TimeSpan.FromSeconds(30))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Reconciliation worker background loop is stalled. Last activity was {silenceDuration.TotalSeconds:F1} seconds ago."));
            }

            return Task.FromResult(HealthCheckResult.Healthy($"Trading Engine is healthy and active. Reconciliation last ran {silenceDuration.TotalSeconds:F1} seconds ago."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Trading Engine health check failed with exception.", ex));
        }
    }
}
