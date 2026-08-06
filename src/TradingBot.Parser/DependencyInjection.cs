using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Pipeline;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Extractors;

namespace Microsoft.Extensions.DependencyInjection;

public static class ParserDependencyInjection
{
    public static IServiceCollection AddParser(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<ParserOptions>(configuration.GetSection(ParserOptions.SectionName));
        }
        else
        {
            services.Configure<ParserOptions>(_ => { });
        }

        // Register individual extractors in sequence
        services.AddScoped<ISignalExtractor, SymbolExtractor>();
        services.AddScoped<ISignalExtractor, DirectionExtractor>();
        services.AddScoped<ISignalExtractor, EntryExtractor>();
        services.AddScoped<ISignalExtractor, StopLossExtractor>();
        services.AddScoped<ISignalExtractor, TakeProfitExtractor>();
        services.AddScoped<ISignalExtractor, LeverageExtractor>();

        services.AddScoped<IParserPipeline, SignalParserPipeline>();
        services.AddScoped<ISignalParser, DefaultSignalParser>();

        return services;
    }
}
