using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Services;

public static class PositionLockManager
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public static async Task<IDisposable> AcquireLockAsync(Guid positionId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(positionId, _ => new SemaphoreSlim(1, 1));

        bool acquired = await semaphore.WaitAsync(timeout, cancellationToken);
        if (!acquired)
        {
            throw new TimeoutException($"Could not acquire position lock for position {positionId} within {timeout.TotalSeconds} seconds.");
        }

        return new Releaser(positionId, semaphore);
    }

    private class Releaser : IDisposable
    {
        private readonly Guid _positionId;
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(Guid positionId, SemaphoreSlim semaphore)
        {
            _positionId = positionId;
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }
}
