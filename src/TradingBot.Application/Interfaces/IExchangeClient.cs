using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces;

public interface IExchangeClient
{
    string ExchangeName { get; }
    Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order> GetOrderStatusAsync(string clientOrderId, string symbol, CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
    Task<decimal> GetAccountBalanceAsync(string coin = "USDT", CancellationToken cancellationToken = default);
    Task<bool> IsSymbolValidAsync(string symbol, CancellationToken cancellationToken = default);
    Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default);
}
