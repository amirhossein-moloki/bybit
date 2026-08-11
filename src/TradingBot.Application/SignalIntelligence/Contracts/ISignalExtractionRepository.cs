using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface ISignalExtractionRepository : IRepository<SignalExtraction>
{
    Task CreateAsync(SignalExtraction extraction, CancellationToken cancellationToken = default);
    Task<SignalExtraction?> GetByMessageIdAsync(long messageId, CancellationToken cancellationToken = default);
}
