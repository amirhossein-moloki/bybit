using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IStartupRecoveryManager
{
    Task RunRecoverySequenceAsync(CancellationToken cancellationToken = default);
}
