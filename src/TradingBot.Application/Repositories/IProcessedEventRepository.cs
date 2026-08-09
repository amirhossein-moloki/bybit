using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface IProcessedEventRepository : IRepository<ProcessedEvent>
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}
