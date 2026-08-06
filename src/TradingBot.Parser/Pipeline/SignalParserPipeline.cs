using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Exceptions;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Pipeline;

public class SignalParserPipeline : IParserPipeline
{
    private readonly IEnumerable<ISignalExtractor> _extractors;
    private readonly IOptions<ParserOptions> _options;
    private readonly ILogger<SignalParserPipeline> _logger;
    private readonly ITemplateManager? _templateManager;

    public SignalParserPipeline(
        IEnumerable<ISignalExtractor> extractors,
        IOptions<ParserOptions> options,
        ILogger<SignalParserPipeline> logger,
        ITemplateManager? templateManager = null)
    {
        _extractors = extractors ?? throw new ArgumentNullException(nameof(extractors));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _templateManager = templateManager;
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

        // Find matching template
        ISignalTemplate? template = null;
        if (_templateManager != null)
        {
            try
            {
                template = await _templateManager.FindTemplateAsync(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Template Execution Error: Parser Warning, Continue Processing (Failed to find matching template)");
            }
        }

        var signal = new ParsedSignal();

        // Bind matched template to async execution context
        TemplateContext.Current = template;

        try
        {
            foreach (var extractor in _extractors)
            {
                var extractorName = extractor.GetType().Name;
                _logger.LogInformation("Extractor Started: {ExtractorName}", extractorName);

                try
                {
                    await extractor.ExtractAsync(context, signal);

                    // Log allowed extraction information safely
                    if (extractorName == "SymbolExtractor" && signal.Symbol != null)
                    {
                        _logger.LogInformation("Symbol Extracted: {Symbol}", signal.Symbol);
                    }
                    else if (extractorName == "EntryExtractor" && signal.EntryPrice != null)
                    {
                        _logger.LogInformation("Price Extracted: {Price}", signal.EntryPrice);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Extractor {ExtractorName} failed for SignalId: {SignalId}", extractorName, context.SignalId);
                    signal.Errors.Add($"Extractor {extractorName} failed: {ex.Message}");
                }
            }

            // Verify required template rules
            if (template != null)
            {
                var templateName = (template is SignalTemplate st) ? st.Name : "Default";
                foreach (var rule in template.GetRules())
                {
                    if (rule.Required)
                    {
                        var isMissing = rule.Field switch
                        {
                            "Symbol" => signal.Symbol == null,
                            "Side" => signal.Side == null,
                            "EntryPrice" => signal.EntryPrice == null,
                            "StopLoss" => signal.StopLoss == null,
                            "TakeProfits" => signal.TakeProfits == null || !signal.TakeProfits.Any(),
                            "Leverage" => signal.Leverage == null,
                            _ => false
                        };

                        if (isMissing)
                        {
                            _logger.LogWarning("Template Execution Error: Required field {Field} missing for template {TemplateName}.", rule.Field, templateName);
                            signal.Warnings.Add($"Required template field {rule.Field} was not extracted.");
                        }
                    }
                }

                _logger.LogInformation("Template Applied\nChannel:\n{ChannelId}\nTemplate:\n{TemplateName}", context.SourceChannel, templateName);
            }
        }
        catch (Exception ex)
        {
            var templateName = (template is SignalTemplate st) ? st.Name : "Default";
            _logger.LogError(ex, "Template Failed\nChannel:\n{ChannelId}\nTemplate:\n{TemplateName}\nError:\n{ErrorMessage}", context.SourceChannel, templateName, ex.Message);
            throw;
        }
        finally
        {
            // Reset active template context
            TemplateContext.Current = null;
        }

        _logger.LogInformation("Pipeline execution completed for SignalId: {SignalId}", context.SignalId);
        return signal;
    }
}
