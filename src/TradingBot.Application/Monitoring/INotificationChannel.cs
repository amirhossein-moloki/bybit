using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface INotificationChannel
{
    string ChannelName { get; }
    Task<NotificationDeliveryResult> SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
