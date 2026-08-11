using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Pipeline;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Extractors;
using TradingBot.Parser.Templates;
using TradingBot.Parser.Validation;
using TradingBot.Parser.Validation.Rules;

namespace Microsoft.Extensions.DependencyInjection;

public static class ParserDependencyInjection
{
    public static IServiceCollection AddParser(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<ParserOptions>(configuration.GetSection(ParserOptions.SectionName));
            services.Configure<ParserTemplatesOptions>(configuration.GetSection(ParserTemplatesOptions.SectionName));
            services.Configure<ValidationOptions>(configuration.GetSection(ValidationOptions.SectionName));
        }
        else
        {
            services.Configure<ParserOptions>(_ => { });
            services.Configure<ParserTemplatesOptions>(_ => { });
            services.Configure<ValidationOptions>(_ => { });
        }

        // Register individual extractors in sequence
        services.AddScoped<ISignalExtractor, SymbolExtractor>();
        services.AddScoped<ISignalExtractor, DirectionExtractor>();
        services.AddScoped<ISignalExtractor, EntryExtractor>();
        services.AddScoped<ISignalExtractor, StopLossExtractor>();
        services.AddScoped<ISignalExtractor, TakeProfitExtractor>();
        services.AddScoped<ISignalExtractor, LeverageExtractor>();

        // Register template engine services
        services.AddSingleton<DefaultSignalTemplate>();
        services.AddScoped<ITemplateManager, TemplateManager>();

        services.AddScoped<IParserPipeline, SignalParserPipeline>();
        services.AddScoped<ISignalParser, DefaultSignalParser>();
        services.AddScoped<IMessageParser, MessageParser>();

        // Register Validation Engine and Rules
        services.AddScoped<IValidationRule, SymbolValidationRule>();
        services.AddScoped<IValidationRule, DirectionValidationRule>();
        services.AddScoped<IValidationRule, EntryValidationRule>();
        services.AddScoped<IValidationRule, StopLossValidationRule>();
        services.AddScoped<IValidationRule, TakeProfitValidationRule>();
        services.AddScoped<IValidationRule, LeverageValidationRule>();
        services.AddScoped<IValidationRule, BusinessConsistencyValidationRule>();
        services.AddScoped<ISignalValidator, ValidationEngine>();

        return services;
    }
}
