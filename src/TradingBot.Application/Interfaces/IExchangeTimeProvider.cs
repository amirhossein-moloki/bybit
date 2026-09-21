using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

/// <summary>
/// Provides exchange server time synchronization and offset-adjusted Unix timestamp calculations in milliseconds.
/// </summary>
public interface IExchangeTimeProvider
{
    /// <summary>
    /// Gets the current Unix timestamp in milliseconds synchronized with the exchange server time offset.
    /// </summary>
    /// <returns>Current Unix timestamp in milliseconds adjusted for server offset (LocalUtcNow + OffsetMs).</returns>
    long GetCurrentMilliseconds();

    /// <summary>
    /// Synchronizes local time with the exchange server time and updates the stored offset in memory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SyncTimeAsync(CancellationToken cancellationToken = default);
}
