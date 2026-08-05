using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Infrastructure.Persistence;

public class SignalRepository : ISignalRepository
{
    private readonly TradingDbContext _dbContext;

    public SignalRepository(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SaveAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Signals.FindAsync(new object?[] { signal.Id }, cancellationToken);
        if (existing == null)
        {
            await _dbContext.Signals.AddAsync(signal, cancellationToken);
        }
        else
        {
            _dbContext.Signals.Update(signal);
        }
    }

    public async Task<Signal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Signals.FindAsync(new object?[] { id }, cancellationToken);
    }
}
