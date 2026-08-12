using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;

namespace TradingBot.Persistence.SignalIntelligence.Repositories;

public class FailedMessageAnalysisRepository : RepositoryBase<FailedMessageAnalysis>, IFailedMessageAnalysisRepository
{
    public FailedMessageAnalysisRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(FailedMessageAnalysis failedAnalysis, CancellationToken cancellationToken = default)
    {
        await AddAsync(failedAnalysis, cancellationToken);
    }

    public async Task<FailedMessageAnalysis?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<FailedMessageAnalysis>()
            .FirstOrDefaultAsync(f => f.MessageId == messageId, cancellationToken);
    }

    public new void Update(FailedMessageAnalysis failedAnalysis)
    {
        base.Update(failedAnalysis);
    }
}
