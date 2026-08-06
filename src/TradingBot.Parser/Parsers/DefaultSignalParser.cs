using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Exceptions;

namespace TradingBot.Parser.Parsers;

public class DefaultSignalParser : ISignalParser
{
    private readonly IParserPipeline _pipeline;
    private readonly IOptions<ParserOptions> _options;
    private readonly ILogger<DefaultSignalParser> _logger;

    public DefaultSignalParser(
        IParserPipeline pipeline,
        IOptions<ParserOptions> options,
        ILogger<DefaultSignalParser> logger)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ParserResult> ParseAsync(ParserContext context)
    {
        if (context == null)
        {
            _logger.LogError("Parser Failed: context is null.");
            return ParserResult.Failure(
                new[] { "ParserContext cannot be null." },
                _options.Value?.Version ?? "1.0"
            );
        }

        _logger.LogInformation("Parsing Started for SignalId: {SignalId}", context.SignalId);

        try
        {
            var parsedSignal = await _pipeline.ExecuteAsync(context);

            _logger.LogInformation("Parsing Completed for SignalId: {SignalId}", context.SignalId);

            return ParserResult.SuccessResult(parsedSignal, context.ParserVersion);
        }
        catch (ParserException ex)
        {
            _logger.LogError(ex, "Parser Failed for SignalId: {SignalId}. Message: {Message}", context.SignalId, ex.Message);
            return ParserResult.Failure(
                new[] { ex.Message },
                context.ParserVersion
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parser Failed unexpectedly for SignalId: {SignalId}.", context.SignalId);
            return ParserResult.Failure(
                new[] { "An unexpected error occurred during parsing." },
                context.ParserVersion
            );
        }
    }
}
