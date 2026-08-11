using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;

namespace TradingBot.Persistence.SignalIntelligence.Repositories;

public class MessageAnalysisRepository : RepositoryBase<MessageAnalysis>, IMessageAnalysisRepository
{
    public MessageAnalysisRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(MessageAnalysis analysis, CancellationToken cancellationToken = default)
    {
        await AddAsync(analysis, cancellationToken);
    }

    public async Task<MessageAnalysis?> GetByMessageIdAsync(Guid telegramMessageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<MessageAnalysis>()
            .FirstOrDefaultAsync(a => a.TelegramMessageId == telegramMessageId, cancellationToken);
    }
}
