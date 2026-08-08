using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IOrderReconciliationService
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
