using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageProcessingAttemptRepository : IRepository<MessageProcessingAttempt>
{
    Task CreateAsync(MessageProcessingAttempt attempt, CancellationToken cancellationToken = default);
    Task UpdateAsync(MessageProcessingAttempt attempt, CancellationToken cancellationToken = default);
    Task<List<MessageProcessingAttempt>> GetByTelegramMessageIdAsync(Guid telegramMessageId, CancellationToken cancellationToken = default);
    Task<MessageProcessingAttempt?> GetActiveAttemptAsync(Guid telegramMessageId, CancellationToken cancellationToken = default);
    Task<MessageProcessingAttempt?> GetLatestAttemptAsync(Guid telegramMessageId, CancellationToken cancellationToken = default);
}
