using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Infrastructure.Monitoring.Checks;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly TradingDbContext _dbContext;

    public string Name => "Database";

    public DatabaseHealthCheck(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            stopwatch.Stop();

            if (canConnect)
            {
                await _dbContext.Symbols.AnyAsync(cancellationToken);

                return new HealthCheckResult(
                    Name,
                    HealthStatus.Healthy,
                    DateTime.UtcNow,
                    stopwatch.ElapsedMilliseconds,
                    metadata: $"{{\"ResponseTimeMs\":{stopwatch.ElapsedMilliseconds}}}"
                );
            }

            return new HealthCheckResult(
                Name,
                HealthStatus.Unhealthy,
                DateTime.UtcNow,
                stopwatch.ElapsedMilliseconds,
                errorCode: "CONNECT_FAILED",
                errorMessage: "Cannot connect to the database."
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new HealthCheckResult(
                Name,
                HealthStatus.Unhealthy,
                DateTime.UtcNow,
                stopwatch.ElapsedMilliseconds,
                errorCode: ex.GetType().Name,
                errorMessage: $"Database connection failed: {ex.Message}"
            );
        }
    }
}
