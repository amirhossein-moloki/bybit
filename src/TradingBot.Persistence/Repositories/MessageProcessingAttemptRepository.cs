using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class MessageProcessingAttemptRepository : RepositoryBase<MessageProcessingAttempt>, IMessageProcessingAttemptRepository
{
    public MessageProcessingAttemptRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(MessageProcessingAttempt attempt, CancellationToken cancellationToken = default)
    {
        await AddAsync(attempt, cancellationToken);
    }

    public async Task UpdateAsync(MessageProcessingAttempt attempt, CancellationToken cancellationToken = default)
    {
        base.Update(attempt);
        await Task.CompletedTask;
    }

    public override async Task<MessageProcessingAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<MessageProcessingAttempt>()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<List<MessageProcessingAttempt>> GetByTelegramMessageIdAsync(Guid telegramMessageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<MessageProcessingAttempt>()
            .Where(a => a.TelegramMessageId == telegramMessageId)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<MessageProcessingAttempt?> GetActiveAttemptAsync(Guid telegramMessageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<MessageProcessingAttempt>()
            .FirstOrDefaultAsync(a => a.TelegramMessageId == telegramMessageId && a.Status == "InProgress", cancellationToken);
    }

    public async Task<MessageProcessingAttempt?> GetLatestAttemptAsync(Guid telegramMessageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<MessageProcessingAttempt>()
            .Where(a => a.TelegramMessageId == telegramMessageId)
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
