using System;

namespace TradingBot.Domain.Entities;

public class StopLossHistory
{
    public Guid Id { get; private set; }
    public Guid PositionId { get; private set; }
    public decimal? OldPrice { get; private set; }
    public decimal? NewPrice { get; private set; }
    public string Reason { get; private set; }
    public string Source { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private StopLossHistory()
    {
        Id = Guid.Empty;
        PositionId = Guid.Empty;
        Reason = string.Empty;
        Source = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public StopLossHistory(Guid positionId, decimal? oldPrice, decimal? newPrice, string reason, string source = "System")
    {
        Id = Guid.Empty; // Let EF Core generate the Guid automatically on Add to prevent tracking conflicts
        PositionId = positionId;
        OldPrice = oldPrice;
        NewPrice = newPrice;
        Reason = reason ?? string.Empty;
        Source = source ?? "System";
        CreatedAt = DateTime.UtcNow;
    }
}
