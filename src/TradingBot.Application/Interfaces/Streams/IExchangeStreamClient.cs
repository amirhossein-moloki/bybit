using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Enums;

namespace TradingBot.Application.Interfaces.Streams;

public interface IExchangeStreamClient
{
    IMarketStream MarketStream { get; }
    IOrderStream OrderStream { get; }
    IPositionStream PositionStream { get; }
    ConnectionState State { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
