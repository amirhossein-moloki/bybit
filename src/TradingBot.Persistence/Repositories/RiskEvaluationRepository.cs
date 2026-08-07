using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Application.Repositories;

namespace TradingBot.Persistence.Repositories;

public class RiskEvaluationRepository : RepositoryBase<RiskEvaluation>, IRiskEvaluationRepository
{
    public RiskEvaluationRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
