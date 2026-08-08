using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IExchangeTradingGateway
{
    Task<OrderResult> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderResult> GetOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default);
    Task<OrderResult> CancelOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default);
}
