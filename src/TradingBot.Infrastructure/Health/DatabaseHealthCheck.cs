using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Infrastructure.Persistence;

namespace TradingBot.Infrastructure.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly TradingBotDbContext _dbContext;

    public DatabaseHealthCheck(TradingBotDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection is healthy.");
            }
            return HealthCheckResult.Unhealthy("Cannot connect to the database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database health check failed with an exception.", ex);
        }
    }
}
