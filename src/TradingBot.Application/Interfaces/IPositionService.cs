using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Interfaces;

public interface IPositionService
{
    Task<Position> CreatePositionFromOrderAsync(
        Order order,
        IEnumerable<PositionTarget>? targets = null,
        CancellationToken cancellationToken = default);

    Task<Position?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Position?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default);
    Task UpdatePositionStatusAsync(Guid id, PositionStatus newStatus, string reason = "", CancellationToken cancellationToken = default);
    Task AddPositionEventAsync(Guid positionId, string eventType, string payload, CancellationToken cancellationToken = default);
}
