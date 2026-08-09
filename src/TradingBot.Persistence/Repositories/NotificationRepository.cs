using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class NotificationRepository : RepositoryBase<Notification>, INotificationRepository
{
    public NotificationRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public override async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbContext.Notifications
            .Include(x => x.DeliveryAttempts)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Notification>> GetPendingAndRetryScheduledAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        return await DbContext.Notifications
            .Include(x => x.DeliveryAttempts)
            .Where(x => x.Status == NotificationStatus.Pending ||
                        (x.Status == NotificationStatus.RetryScheduled && x.NextAttemptAt <= utcNow))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsForEventAsync(Guid eventId, string channel, string recipient, CancellationToken cancellationToken = default)
    {
        return await DbContext.Notifications
            .AnyAsync(x => x.EventId == eventId &&
                           x.Channel == channel &&
                           x.Recipient == recipient, cancellationToken);
    }
}
