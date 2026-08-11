using System.Collections.Generic;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public class ParsedMessageResult
{
    public MessageType Type { get; set; } = MessageType.UNKNOWN;
    public string? Symbol { get; set; }
    public OrderSide? Side { get; set; }
    public decimal? Entry { get; set; }
    public decimal? EntryRangeMin { get; set; }
    public decimal? EntryRangeMax { get; set; }
    public decimal? StopLoss { get; set; }
    public IReadOnlyList<decimal> TakeProfits { get; set; } = new List<decimal>();
    public TradeAction? Action { get; set; }
    public decimal Confidence { get; set; }
    public ParserSource Source { get; set; } = ParserSource.RULE_BASED;
    public IReadOnlyList<string> DetectedFields { get; set; } = new List<string>();
    public string? ErrorMessage { get; set; }
}
