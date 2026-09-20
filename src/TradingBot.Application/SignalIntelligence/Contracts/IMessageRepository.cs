using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageRepository : IRepository<TelegramMessage>
{
    Task CreateAsync(TelegramMessage message, CancellationToken cancellationToken = default);
    new Task<TelegramMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TelegramMessage?> GetByChannelMessageIdAsync(long channelId, long messageId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<TelegramMessage>> GetRecentMessagesForChannelAsync(long channelId, int limit, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
    Task<TelegramMessage?> GetLastReceivedMessageAsync(CancellationToken cancellationToken = default);
    Task<TelegramMessage?> GetLastProcessedMessageAsync(CancellationToken cancellationToken = default);
}
