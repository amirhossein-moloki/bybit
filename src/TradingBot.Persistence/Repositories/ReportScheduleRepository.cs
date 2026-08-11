using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class ReportScheduleRepository : RepositoryBase<ReportSchedule>, IReportScheduleRepository
{
    public ReportScheduleRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}
