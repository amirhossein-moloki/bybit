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

using TradingBot.Application.Monitoring;

public class NotificationRepository : RepositoryBase<Notification>, INotificationRepository
{
    private static readonly SemaphoreSlim SqliteLock = new(1, 1);

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
        var staleProcessingThreshold = utcNow.AddMinutes(-2);
        return await DbContext.Notifications
            .Include(x => x.DeliveryAttempts)
            .Where(x => x.Status == NotificationStatus.Pending ||
                        (x.Status == NotificationStatus.RetryScheduled && x.NextAttemptAt <= utcNow) ||
                        (x.Status == NotificationStatus.Processing && x.LastAttemptAt != null && x.LastAttemptAt <= staleProcessingThreshold))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsForEventAsync(Guid eventId, string channel, string recipient, CancellationToken cancellationToken = default)
    {
        return await DbContext.Notifications
            .AnyAsync(x => x.EventId == eventId &&
                           x.Channel == channel &&
                           x.Recipient == recipient, cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> ClaimPendingNotificationsAsync(int batchSize, string workerId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var staleThreshold = now.AddMinutes(-2);

        if (DbContext.Database.IsNpgsql())
        {
            // PostgreSQL implementation with FOR UPDATE SKIP LOCKED
            var claimed = await DbContext.Notifications
                .FromSqlInterpolated($@"
                    UPDATE ""Notifications""
                    SET ""Status"" = 'Processing',
                        ""AttemptCount"" = ""AttemptCount"" + 1,
                        ""LastAttemptAt"" = {now},
                        ""UpdatedAt"" = {now}
                    WHERE ""Id"" IN (
                        SELECT ""Id""
                        FROM ""Notifications""
                        WHERE ""Status"" = 'Pending'
                           OR (""Status"" = 'RetryScheduled' AND ""NextAttemptAt"" <= {now})
                           OR (""Status"" = 'Processing' AND ""LastAttemptAt"" <= {staleThreshold})
                        ORDER BY ""CreatedAt""
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    RETURNING *;
                ")
                .ToListAsync(cancellationToken);

            return claimed;
        }

        // Fallback for non-PostgreSQL (SQLite/In-Memory used in unit/integration tests)
        await SqliteLock.WaitAsync(cancellationToken);
        try
        {
            var eligible = await DbContext.Notifications
                .Where(x => x.Status == NotificationStatus.Pending ||
                            (x.Status == NotificationStatus.RetryScheduled && x.NextAttemptAt <= now) ||
                            (x.Status == NotificationStatus.Processing && x.LastAttemptAt <= staleThreshold))
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var claimedList = new List<Notification>();
            foreach (var notification in eligible)
            {
                try
                {
                    notification.MarkProcessing();
                    await DbContext.SaveChangesAsync(cancellationToken);
                    claimedList.Add(notification);
                }
                catch (DbUpdateConcurrencyException)
                {
                    DbContext.Entry(notification).State = EntityState.Detached;
                }
            }

            return claimedList;
        }
        finally
        {
            SqliteLock.Release();
        }
    }

    public async Task<bool> TryUpdateDeliveryResultAsync(
        Guid notificationId,
        NotificationDeliveryResult result,
        int initialRetryDelaySeconds,
        int maxRetryDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        if (DbContext.Database.IsNpgsql())
        {
            var notification = await DbContext.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == notificationId, cancellationToken);

            if (notification == null || notification.Status != NotificationStatus.Processing)
            {
                return false;
            }

            string newStatus;
            DateTime? deliveredAt = null;
            DateTime? nextAttemptAt = null;
            DateTime? failedAt = null;
            string? failureReason = null;

            if (result.Success)
            {
                newStatus = NotificationStatus.Delivered.ToString();
                deliveredAt = now;
            }
            else if (result.IsRetryable && notification.AttemptCount < notification.MaxAttempts)
            {
                newStatus = NotificationStatus.RetryScheduled.ToString();

                var baseDelay = initialRetryDelaySeconds <= 0 ? 2 : initialRetryDelaySeconds;
                var maxDelay = maxRetryDelaySeconds <= 0 ? 60 : maxRetryDelaySeconds;
                var backoffSeconds = baseDelay * Math.Pow(2, notification.AttemptCount - 1);
                var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2;
                backoffSeconds = backoffSeconds * (1 + jitter);

                var finalDelaySeconds = Math.Min(backoffSeconds, maxDelay);
                if (finalDelaySeconds < 1) finalDelaySeconds = 1;

                nextAttemptAt = now.AddSeconds(finalDelaySeconds);
                failureReason = result.SafeMessage ?? "Transient error";
            }
            else
            {
                newStatus = NotificationStatus.Failed.ToString();
                failedAt = now;
                failureReason = result.SafeMessage ?? "Max attempts exceeded or permanent error.";
            }

            var rowsAffected = await DbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Notifications""
                SET ""Status"" = {newStatus},
                    ""DeliveredAt"" = {deliveredAt},
                    ""NextAttemptAt"" = {nextAttemptAt},
                    ""FailedAt"" = {failedAt},
                    ""FailureReason"" = {failureReason},
                    ""UpdatedAt"" = {now}
                WHERE ""Id"" = {notificationId}
                  AND ""Status"" = 'Processing';
            ", cancellationToken);

            if (rowsAffected > 0)
            {
                var attempt = new NotificationDeliveryAttempt(
                    notificationId: notificationId,
                    attemptNumber: notification.AttemptCount,
                    isSuccess: result.Success,
                    errorCode: result.ErrorCode,
                    errorMessage: result.SafeMessage
                );

                await DbContext.NotificationDeliveryAttempts.AddAsync(attempt, cancellationToken);
                await DbContext.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }

        // Fallback for SQLite / test environments
        await SqliteLock.WaitAsync(cancellationToken);
        try
        {
            var notification = await DbContext.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == notificationId, cancellationToken);

            if (notification == null || notification.Status != NotificationStatus.Processing)
            {
                return false;
            }

            string newStatus;
            DateTime? deliveredAt = null;
            DateTime? nextAttemptAt = null;
            DateTime? failedAt = null;
            string? failureReason = null;

            if (result.Success)
            {
                newStatus = NotificationStatus.Delivered.ToString();
                deliveredAt = now;
            }
            else if (result.IsRetryable && notification.AttemptCount < notification.MaxAttempts)
            {
                newStatus = NotificationStatus.RetryScheduled.ToString();

                var baseDelay = initialRetryDelaySeconds <= 0 ? 2 : initialRetryDelaySeconds;
                var maxDelay = maxRetryDelaySeconds <= 0 ? 60 : maxRetryDelaySeconds;
                var backoffSeconds = baseDelay * Math.Pow(2, notification.AttemptCount - 1);
                var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2;
                backoffSeconds = backoffSeconds * (1 + jitter);

                var finalDelaySeconds = Math.Min(backoffSeconds, maxDelay);
                if (finalDelaySeconds < 1) finalDelaySeconds = 1;

                nextAttemptAt = now.AddSeconds(finalDelaySeconds);
                failureReason = result.SafeMessage ?? "Transient error";
            }
            else
            {
                newStatus = NotificationStatus.Failed.ToString();
                failedAt = now;
                failureReason = result.SafeMessage ?? "Max attempts exceeded or permanent error.";
            }

            var rowsAffected = await DbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Notifications""
                SET ""Status"" = {newStatus},
                    ""DeliveredAt"" = {deliveredAt},
                    ""NextAttemptAt"" = {nextAttemptAt},
                    ""FailedAt"" = {failedAt},
                    ""FailureReason"" = {failureReason},
                    ""UpdatedAt"" = {now}
                WHERE ""Id"" = {notificationId}
                  AND ""Status"" = 'Processing';
            ", cancellationToken);

            if (rowsAffected > 0)
            {
                var attempt = new NotificationDeliveryAttempt(
                    notificationId: notificationId,
                    attemptNumber: notification.AttemptCount,
                    isSuccess: result.Success,
                    errorCode: result.ErrorCode,
                    errorMessage: result.SafeMessage
                );

                await DbContext.NotificationDeliveryAttempts.AddAsync(attempt, cancellationToken);
                await DbContext.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }
        finally
        {
            SqliteLock.Release();
        }
    }
}
