using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IPositionRecoveryService
{
    Task RecoverPositionsAsync(CancellationToken cancellationToken = default);
}
