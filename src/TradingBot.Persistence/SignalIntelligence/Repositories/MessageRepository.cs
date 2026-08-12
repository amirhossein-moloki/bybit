using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;

namespace TradingBot.Persistence.SignalIntelligence.Repositories;

public class MessageRepository : RepositoryBase<TelegramMessage>, IMessageRepository
{
    public MessageRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(TelegramMessage message, CancellationToken cancellationToken = default)
    {
        await AddAsync(message, cancellationToken);
    }

    public override async Task<TelegramMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<TelegramMessage>().FindAsync(new object?[] { id }, cancellationToken);
    }

    public async Task<TelegramMessage?> GetByChannelMessageIdAsync(long channelId, long messageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<TelegramMessage>()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.MessageId == messageId, cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var message = await GetByIdAsync(id, cancellationToken);
        if (message != null)
        {
            message.MarkProcessed();
            Update(message);
        }
    }

    public async Task<System.Collections.Generic.List<TelegramMessage>> GetRecentMessagesForChannelAsync(long channelId, int limit, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<TelegramMessage>()
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.ReceivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
