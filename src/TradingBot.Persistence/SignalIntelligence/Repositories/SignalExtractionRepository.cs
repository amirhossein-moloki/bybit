using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;

namespace TradingBot.Persistence.SignalIntelligence.Repositories;

public class SignalExtractionRepository : RepositoryBase<SignalExtraction>, ISignalExtractionRepository
{
    public SignalExtractionRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task CreateAsync(SignalExtraction extraction, CancellationToken cancellationToken = default)
    {
        await AddAsync(extraction, cancellationToken);
    }

    public async Task<SignalExtraction?> GetByMessageIdAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<SignalExtraction>()
            .FirstOrDefaultAsync(e => e.MessageId == messageId, cancellationToken);
    }
}
