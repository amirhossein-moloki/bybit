using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageProcessingTrackerRepository : IRepository<MessageProcessingTracker>
{
    Task CreateAsync(MessageProcessingTracker tracker, CancellationToken cancellationToken = default);
    Task<MessageProcessingTracker?> GetByTelegramMessageIdAsync(Guid telegramMessageId, CancellationToken cancellationToken = default);
    new void Update(MessageProcessingTracker tracker);
}
