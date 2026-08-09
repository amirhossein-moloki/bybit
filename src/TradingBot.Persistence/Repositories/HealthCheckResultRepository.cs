using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class HealthCheckResultRepository : RepositoryBase<HealthCheckResult>, IHealthCheckResultRepository
{
    public HealthCheckResultRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
