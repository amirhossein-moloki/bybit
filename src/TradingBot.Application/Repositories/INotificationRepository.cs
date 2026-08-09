using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface INotificationRepository : IRepository<Notification>
{
    Task<IEnumerable<Notification>> GetPendingAndRetryScheduledAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsForEventAsync(Guid eventId, string channel, string recipient, CancellationToken cancellationToken = default);
}
