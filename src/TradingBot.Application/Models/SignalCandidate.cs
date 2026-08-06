using System;

namespace TradingBot.Application.Models;

public class SignalCandidate
{
    public long ChannelId { get; set; }
    public int MessageId { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string DetectedSymbol { get; set; } = string.Empty;
    public string DetectedSide { get; set; } = string.Empty;
    public int DetectionScore { get; set; }
    public DateTime DetectedAt { get; set; }
}
