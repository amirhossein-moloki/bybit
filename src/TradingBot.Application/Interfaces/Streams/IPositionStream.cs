using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models.Events;

namespace TradingBot.Application.Interfaces.Streams;

public interface IPositionStream
{
    Task SubscribeAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<PositionUpdateEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default);
}
