using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Pipeline;
using TradingBot.Parser.Parsers;

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

        services.AddScoped<IParserPipeline, SignalParserPipeline>();
        services.AddScoped<ISignalParser, DefaultSignalParser>();

        return services;
    }
}
