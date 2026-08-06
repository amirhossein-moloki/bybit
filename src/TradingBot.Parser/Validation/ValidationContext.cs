using System;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Validation;

public class ValidationContext
{
    public Guid SignalId { get; }
    public ParsedSignal ParsedSignal { get; }
    public string SourceChannel { get; }
    public string TemplateName { get; }
    public string ParserVersion { get; }
    public DateTime ValidationStartedAt { get; }

    public ValidationContext(Guid signalId, ParsedSignal parsedSignal, string sourceChannel, string templateName, string parserVersion)
    {
        SignalId = signalId;
        ParsedSignal = parsedSignal ?? throw new ArgumentNullException(nameof(parsedSignal));
        SourceChannel = sourceChannel ?? "UNKNOWN";
        TemplateName = templateName ?? "Default";
        ParserVersion = parserVersion ?? "1.0";
        ValidationStartedAt = DateTime.UtcNow;
    }
}
