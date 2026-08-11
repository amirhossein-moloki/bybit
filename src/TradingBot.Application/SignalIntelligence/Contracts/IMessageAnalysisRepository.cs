using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageAnalysisRepository : IRepository<MessageAnalysis>
{
    Task CreateAsync(MessageAnalysis analysis, CancellationToken cancellationToken = default);
    Task<MessageAnalysis?> GetByMessageIdAsync(Guid telegramMessageId, CancellationToken cancellationToken = default);
}
