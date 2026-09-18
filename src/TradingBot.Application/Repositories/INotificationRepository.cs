using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

using TradingBot.Application.Monitoring;

public interface INotificationRepository : IRepository<Notification>
{
    Task<IEnumerable<Notification>> GetPendingAndRetryScheduledAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsForEventAsync(Guid eventId, string channel, string recipient, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> ClaimPendingNotificationsAsync(int batchSize, string workerId, CancellationToken cancellationToken = default);
    Task<bool> TryUpdateDeliveryResultAsync(Guid notificationId, NotificationDeliveryResult result, int initialRetryDelaySeconds, int maxRetryDelaySeconds, CancellationToken cancellationToken = default);
}
