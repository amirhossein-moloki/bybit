using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;

namespace TradingBot.Infrastructure.Persistence;

public class InMemorySignalRepository : ISignalRepository
{
    private static readonly ConcurrentDictionary<Guid, Signal> _signals = new();

    public Task SaveAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        _signals[signal.Id] = signal;
        return Task.CompletedTask;
    }

    public Task<Signal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _signals.TryGetValue(id, out var signal);
        return Task.FromResult<Signal?>(signal);
    }
}
