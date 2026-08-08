using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface IOrderEventRepository : IRepository<OrderEvent>
{
    Task<IEnumerable<OrderEvent>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
