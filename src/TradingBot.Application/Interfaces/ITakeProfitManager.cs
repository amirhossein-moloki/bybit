using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces;

public interface ITakeProfitManager
{
    Task<List<PositionTarget>> CreateTakeProfitTargetsAsync(
        Guid positionId,
        List<(decimal Price, decimal Percentage)> targets,
        CancellationToken cancellationToken = default);
}
