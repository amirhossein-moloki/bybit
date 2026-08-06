using System;
using System.Collections.Generic;
using TradingBot.Domain.Enums;

namespace TradingBot.Domain.RiskManagement.ValueObjects;

public record TradeRiskContext
{
    public Guid SignalId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public OrderSide Side { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? StopLoss { get; init; }
    public IReadOnlyList<decimal> TakeProfits { get; init; } = Array.Empty<decimal>();
    public int? Leverage { get; init; }
    public decimal AccountBalance { get; init; }
    public int OpenPositions { get; init; }
    public decimal DailyPnL { get; init; }
    public decimal CurrentExposure { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
