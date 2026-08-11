using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;

namespace TradingBot.Persistence.SignalIntelligence.Repositories;

public class SignalContextRepository : RepositoryBase<SignalContext>, ISignalContextRepository
{
    public SignalContextRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(SignalContext context, CancellationToken cancellationToken = default)
    {
        await AddAsync(context, cancellationToken);
    }

    public async Task<SignalContext?> GetActiveContextAsync(long channelId, string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = symbol?.Trim().ToUpperInvariant();

        return await DbContext.Set<SignalContext>()
            .FirstOrDefaultAsync(c =>
                c.ChannelId == channelId &&
                c.Symbol == normalizedSymbol &&
                c.CurrentState != SignalState.CLOSED &&
                c.CurrentState != SignalState.CANCELLED,
                cancellationToken);
    }

    public async Task UpdateStateAsync(Guid id, SignalState state, string? lastAction, long lastMessageId, CancellationToken cancellationToken = default)
    {
        var context = await GetByIdAsync(id, cancellationToken);
        if (context != null)
        {
            context.UpdateState(state, lastAction, lastMessageId);
            Update(context);
        }
    }
}
