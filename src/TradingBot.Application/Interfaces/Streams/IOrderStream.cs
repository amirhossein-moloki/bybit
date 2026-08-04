using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models.Events;

namespace TradingBot.Application.Interfaces.Streams;

public interface IOrderStream
{
    Task SubscribeAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<OrderUpdateEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default);
}
