using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface IAlertRepository : IRepository<Alert>
{
    Task<Alert?> GetActiveByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default);
    Task<IEnumerable<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);
}
