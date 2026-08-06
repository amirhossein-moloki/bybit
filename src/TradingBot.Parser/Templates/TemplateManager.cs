using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Domain.Repositories;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Templates;

public class TemplateManager : ITemplateManager
{
    private readonly IParserTemplateRepository? _repository;
    private readonly IOptions<ParserTemplatesOptions> _options;
    private readonly ILogger<TemplateManager> _logger;
    private readonly DefaultSignalTemplate _defaultTemplate;

    public TemplateManager(
        IOptions<ParserTemplatesOptions> options,
        ILogger<TemplateManager> logger,
        DefaultSignalTemplate defaultTemplate,
        IParserTemplateRepository? repository = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultTemplate = defaultTemplate ?? throw new ArgumentNullException(nameof(defaultTemplate));
        _repository = repository;
    }

    public async Task<ISignalTemplate?> FindTemplateAsync(ParserContext context)
    {
        if (context == null) return null;

        var templates = new List<ISignalTemplate>();

        // 1. If DB templates are enabled and repository is available, load from database
        if (_options.Value.EnableDatabaseTemplates && _repository != null)
        {
            try
            {
                var dbEntities = await _repository.GetAllEnabledAsync();
                foreach (var entity in dbEntities)
                {
                    if (entity.Enabled)
                    {
                        try
                        {
                            var template = SignalTemplate.FromEntity(entity);
                            templates.Add(template);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Template Disabled: Template '{TemplateName}' is disabled due to deserialization/invalid rule structure.", entity.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Template Execution Error: Parser Warning, Continue Processing (Failed to load templates from database)");
            }
        }

        // 2. Perform Template Matching Logic
        var matched = TemplateMatcher.Match(templates, context);
        if (matched != null)
        {
            if (matched is SignalTemplate st)
            {
                _logger.LogInformation("Template Selected\nChannel:\n{ChannelId}\nTemplate:\n{TemplateName}", context.SourceChannel, st.Name);
            }
            return matched;
        }

        // 3. Fallback to Default Template
        _logger.LogInformation("Template Not Found for Channel {ChannelId}, falling back to {FallbackTemplate}", context.SourceChannel, _options.Value.FallbackTemplate);
        return _defaultTemplate;
    }
}
