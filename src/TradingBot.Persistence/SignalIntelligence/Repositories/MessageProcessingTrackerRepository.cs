using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;

namespace TradingBot.Persistence.SignalIntelligence.Repositories;

public class MessageProcessingTrackerRepository : RepositoryBase<MessageProcessingTracker>, IMessageProcessingTrackerRepository
{
    public MessageProcessingTrackerRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(MessageProcessingTracker tracker, CancellationToken cancellationToken = default)
    {
        await AddAsync(tracker, cancellationToken);
    }

    public async Task<MessageProcessingTracker?> GetByTelegramMessageIdAsync(Guid telegramMessageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<MessageProcessingTracker>()
            .FirstOrDefaultAsync(t => t.TelegramMessageId == telegramMessageId, cancellationToken);
    }

    public new void Update(MessageProcessingTracker tracker)
    {
        base.Update(tracker);
    }
}
