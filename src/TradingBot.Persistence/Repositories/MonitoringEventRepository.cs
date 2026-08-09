using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class MonitoringEventRepository : RepositoryBase<MonitoringEvent>, IMonitoringEventRepository
{
    public MonitoringEventRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
