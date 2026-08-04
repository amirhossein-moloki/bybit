using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces.Persistence;

public interface ISignalRepository
{
    Task SaveAsync(Signal signal, CancellationToken cancellationToken = default);
    Task<Signal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
