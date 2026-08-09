using System;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Repositories;

public class MonitoringEventByExternalIdSpecification : BaseSpecification<MonitoringEvent>
{
    public MonitoringEventByExternalIdSpecification(string source, string externalEventId)
        : base(x => x.Source == source && x.ExternalEventId == externalEventId)
    {
    }
}
