using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IGracefulShutdownManager
{
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
