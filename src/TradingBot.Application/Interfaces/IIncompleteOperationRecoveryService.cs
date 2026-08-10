using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IIncompleteOperationRecoveryService
{
    Task RecoverIncompleteOperationsAsync(CancellationToken cancellationToken);
}
