using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface ITelegramMessageBuilder
{
    string BuildMessage(MonitoringEvent @event);
}
