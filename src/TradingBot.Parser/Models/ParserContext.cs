using System;
using TradingBot.Parser.Exceptions;

namespace TradingBot.Parser.Models;

public class ParserContext
{
    public Guid SignalId { get; }
    public string RawMessage { get; }
    public string SourceChannel { get; }
    public DateTime ReceivedAt { get; }
    public string ParserVersion { get; }

    public ParserContext(Guid signalId, string rawMessage, string sourceChannel, DateTime receivedAt, string parserVersion, int maxMessageLength = 5000)
    {
        if (signalId == Guid.Empty)
        {
            throw new InvalidParserContextException("SignalId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(sourceChannel))
        {
            throw new InvalidParserContextException("SourceChannel cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(parserVersion))
        {
            throw new InvalidParserContextException("ParserVersion cannot be null or empty.");
        }

        if (rawMessage == null)
        {
            throw new InvalidParserContextException("RawMessage cannot be null.");
        }

        // Basic input sanitization foundation
        // Trim and remove null characters or dangerous control bytes
        var sanitized = rawMessage.Replace("\0", string.Empty).Trim();

        if (string.IsNullOrEmpty(sanitized))
        {
            throw new InvalidParserContextException("RawMessage cannot be empty or whitespace.");
        }

        if (sanitized.Length > maxMessageLength)
        {
            throw new InvalidParserContextException($"RawMessage length exceeds the maximum limit of {maxMessageLength} characters.");
        }

        SignalId = signalId;
        RawMessage = sanitized;
        SourceChannel = sourceChannel;
        ReceivedAt = receivedAt;
        ParserVersion = parserVersion;
    }
}
