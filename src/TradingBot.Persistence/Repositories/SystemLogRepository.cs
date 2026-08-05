using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class SystemLogRepository : RepositoryBase<SystemLog>, ISystemLogRepository
{
    public SystemLogRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
