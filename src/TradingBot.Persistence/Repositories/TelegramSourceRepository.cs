using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class TelegramSourceRepository : RepositoryBase<TelegramSource>, ITelegramSourceRepository
{
    private readonly TradingDbContext _context;

    public TelegramSourceRepository(TradingDbContext context) : base(context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override async Task<TelegramSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TelegramSources
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<TelegramSource?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        return await _context.TelegramSources
            .FirstOrDefaultAsync(x => x.TelegramChatId == chatId, cancellationToken);
    }

    public new async Task<List<TelegramSource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TelegramSources
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TelegramSource>> GetActiveSourcesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.TelegramSources
            .Where(x => x.IsEnabled && (!x.PausedUntil.HasValue || x.PausedUntil.Value <= now))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public override async Task AddAsync(TelegramSource source, CancellationToken cancellationToken = default)
    {
        await _context.TelegramSources.AddAsync(source, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(TelegramSource source, CancellationToken cancellationToken = default)
    {
        Update(source);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(TelegramSource source, CancellationToken cancellationToken = default)
    {
        _context.TelegramSources.Remove(source);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
