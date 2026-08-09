using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface IHealthCheckEngine
{
    Task<IEnumerable<HealthCheckResult>> RunAllChecksAsync(CancellationToken cancellationToken);
}
