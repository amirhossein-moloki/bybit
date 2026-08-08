using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IExchangeTradingGateway
{
    Task<OrderResult> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderResult> GetOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default);
    Task<OrderResult> CancelOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default);
    Task<OrderResult> SetTradingStopAsync(
        string symbol,
        OrderSide side,
        decimal? stopLoss,
        decimal? takeProfit,
        CancellationToken cancellationToken = default);
}
