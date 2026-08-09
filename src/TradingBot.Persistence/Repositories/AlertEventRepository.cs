using System;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class AlertEventRepository : RepositoryBase<AlertEvent>, IAlertEventRepository
{
    public AlertEventRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
