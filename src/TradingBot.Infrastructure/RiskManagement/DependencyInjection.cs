using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Infrastructure.RiskManagement.Configuration;
using TradingBot.Infrastructure.RiskManagement.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class RiskManagementDependencyInjection
{
    public static IServiceCollection AddRiskManagement(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<RiskManagementOptions>(configuration.GetSection(RiskManagementOptions.SectionName));
        }
        else
        {
            services.Configure<RiskManagementOptions>(_ => { });
        }

        // Register core Risk Engine and Support Services
        services.AddScoped<IRiskEngine, RiskEngineService>();
        services.AddScoped<IRiskDecisionService, RiskDecisionService>();

        return services;
    }
}
