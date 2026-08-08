using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IPositionReconciliationService
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
