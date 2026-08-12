using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;

using System.Collections.Generic;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface ISignalContextRepository : IRepository<SignalContext>
{
    Task CreateAsync(SignalContext context, CancellationToken cancellationToken = default);
    Task<SignalContext?> GetActiveContextAsync(long channelId, string symbol, CancellationToken cancellationToken = default);
    Task UpdateStateAsync(Guid id, SignalState state, string? lastAction, long lastMessageId, CancellationToken cancellationToken = default);
    Task<List<SignalContext>> GetActiveContextsForChannelAsync(long channelId, CancellationToken cancellationToken = default);
    Task<SignalContext?> GetLatestActiveContextAsync(long channelId, CancellationToken cancellationToken = default);
}
