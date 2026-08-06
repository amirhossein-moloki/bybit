using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class SymbolRepository : RepositoryBase<Symbol>
{
    public SymbolRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
