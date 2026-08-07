using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Application.Repositories;

namespace TradingBot.Persistence.Repositories;

public class TradeDecisionRepository : RepositoryBase<TradeDecision>, ITradeDecisionRepository
{
    public TradeDecisionRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
