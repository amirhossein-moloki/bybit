using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IPositionSynchronizationService
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}
