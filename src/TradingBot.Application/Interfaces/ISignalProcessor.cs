using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces;

public interface ISignalProcessor
{
    Task ProcessSignalAsync(Signal signal, CancellationToken cancellationToken = default);
}
