using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Application.Repositories;

namespace TradingBot.Persistence.Repositories;

public class RiskProfileRepository : RepositoryBase<RiskProfile>, IRiskProfileRepository
{
    public RiskProfileRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
