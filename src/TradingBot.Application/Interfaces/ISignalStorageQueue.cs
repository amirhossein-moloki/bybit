using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models;

namespace TradingBot.Application.Interfaces;

public interface ISignalStorageQueue
{
    ValueTask EnqueueAsync(SignalCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask<SignalCandidate> DequeueAsync(CancellationToken cancellationToken = default);
}
