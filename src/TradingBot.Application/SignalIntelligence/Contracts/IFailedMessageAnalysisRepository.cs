using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IFailedMessageAnalysisRepository : IRepository<FailedMessageAnalysis>
{
    Task CreateAsync(FailedMessageAnalysis failedAnalysis, CancellationToken cancellationToken = default);
    Task<FailedMessageAnalysis?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);
    new void Update(FailedMessageAnalysis failedAnalysis);
}
