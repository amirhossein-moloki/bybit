using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class ExchangeAccountRepository : RepositoryBase<ExchangeAccount>, IExchangeAccountRepository
{
    public ExchangeAccountRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<ExchangeAccount?> GetByExchangeNameAsync(string exchangeName, CancellationToken cancellationToken = default)
    {
        var normalized = exchangeName.Trim().ToUpperInvariant();
        return await DbContext.ExchangeAccounts
            .FirstOrDefaultAsync(x => x.ExchangeName == normalized, cancellationToken);
    }
}
