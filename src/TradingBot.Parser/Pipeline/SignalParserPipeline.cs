using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Exceptions;

namespace TradingBot.Parser.Pipeline;

public class SignalParserPipeline : IParserPipeline
{
    private readonly IEnumerable<ISignalExtractor> _extractors;
    private readonly IOptions<ParserOptions> _options;
    private readonly ILogger<SignalParserPipeline> _logger;

    public SignalParserPipeline(
        IEnumerable<ISignalExtractor> extractors,
        IOptions<ParserOptions> options,
        ILogger<SignalParserPipeline> logger)
    {
        _extractors = extractors ?? throw new ArgumentNullException(nameof(extractors));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ParsedSignal> ExecuteAsync(ParserContext context)
    {
        if (context == null)
        {
            throw new InvalidParserContextException("Parser context cannot be null during execution.");
        }

        var maxLen = _options.Value?.MaxMessageLength ?? 5000;
        if (context.RawMessage.Length > maxLen)
        {
            throw new InvalidParserContextException($"RawMessage length ({context.RawMessage.Length}) exceeds maximum configured limit of {maxLen} characters.");
        }

        _logger.LogInformation("Pipeline execution started for SignalId: {SignalId}", context.SignalId);

        var signal = new ParsedSignal();

        try
        {
            foreach (var extractor in _extractors)
            {
                _logger.LogDebug("Executing extractor {ExtractorName} for SignalId: {SignalId}", extractor.GetType().Name, context.SignalId);
                await extractor.ExtractAsync(context, signal);
            }
        }
        catch (ParserException)
        {
            // Rethrow parser exceptions directly
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during extractor pipeline execution for SignalId: {SignalId}", context.SignalId);
            throw new ParserExecutionException("An error occurred while executing the parser pipeline.", ex);
        }

        _logger.LogInformation("Pipeline execution completed for SignalId: {SignalId}", context.SignalId);
        return signal;
    }
}
