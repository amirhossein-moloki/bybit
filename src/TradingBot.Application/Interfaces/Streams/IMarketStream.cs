using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models.Events;

namespace TradingBot.Application.Interfaces.Streams;

public interface IMarketStream
{
    Task SubscribeAsync(string symbol, CancellationToken cancellationToken = default);
    IAsyncEnumerable<MarketTickerUpdateEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default);
}
