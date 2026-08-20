using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces.Persistence;

public interface ITelegramSourceRepository
{
    Task<TelegramSource?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TelegramSource?> GetByChatIdAsync(long chatId, CancellationToken ct = default);
    Task<List<TelegramSource>> GetAllAsync(CancellationToken ct = default);
    Task<List<TelegramSource>> GetActiveSourcesAsync(CancellationToken ct = default);
    Task AddAsync(TelegramSource source, CancellationToken ct = default);
    Task UpdateAsync(TelegramSource source, CancellationToken ct = default);
    Task DeleteAsync(TelegramSource source, CancellationToken ct = default);
}
