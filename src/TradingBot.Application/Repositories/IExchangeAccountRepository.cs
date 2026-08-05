using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface IExchangeAccountRepository : IRepository<ExchangeAccount>
{
    Task<ExchangeAccount?> GetByExchangeNameAsync(string exchangeName, CancellationToken cancellationToken = default);
}
