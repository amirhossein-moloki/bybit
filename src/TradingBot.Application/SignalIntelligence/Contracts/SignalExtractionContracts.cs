using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public enum ExtractionValidationStatus
{
    Valid,
    Invalid,
    Partial
}

public class TakeProfitTarget
{
    public int Target { get; set; }
    public decimal Price { get; set; }
}

public class SignalExtractionResult
{
    public bool Success { get; set; }
    public string? Symbol { get; set; }
    public TradeSide Side { get; set; } = TradeSide.UNKNOWN;
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public List<TakeProfitTarget> TakeProfits { get; set; } = new();
    public decimal? Leverage { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public decimal Confidence { get; set; }
    public ExtractionValidationStatus Status { get; set; } = ExtractionValidationStatus.Invalid;
}

public interface IStructuredSignalExtractor
{
    Task<SignalExtractionResult> ExtractAsync(TelegramMessage message, CancellationToken cancellationToken = default);
}
