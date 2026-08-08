using System;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IStopLossManager
{
    Task<bool> UpdateStopLossAsync(
        Guid positionId,
        decimal? stopLoss,
        string reason = "Update",
        string source = "System",
        CancellationToken cancellationToken = default);
}
