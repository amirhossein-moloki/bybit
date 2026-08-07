using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.RiskManagement.Calculators;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Rules;
using TradingBot.Application.RiskManagement.Workflow;
using TradingBot.Infrastructure.RiskManagement.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class RiskManagementDependencyInjection
{
    public static IServiceCollection AddRiskManagement(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<RiskManagementOptions>(configuration.GetSection(RiskManagementOptions.SectionName));
            services.Configure<RiskCalculationOptions>(configuration.GetSection(RiskManagementOptions.SectionName));
        }
        else
        {
            services.Configure<RiskManagementOptions>(_ => { });
            services.Configure<RiskCalculationOptions>(_ => { });
        }

        // Register core Risk Engine and Support Services
        services.AddScoped<IRiskEngine, RiskEngineService>();
        services.AddScoped<IRiskDecisionService, RiskDecisionService>();
        services.AddScoped<IRiskAuditService, RiskAuditService>();
        services.AddScoped<ITradeDecisionWorkflow, TradeDecisionWorkflow>();

        // Register Calculators
        services.AddScoped<RiskAmountCalculator>();
        services.AddScoped<StopLossDistanceCalculator>();
        services.AddScoped<IPositionSizeCalculator, PositionSizeCalculator>();
        services.AddScoped<PositionSizeCalculator>();
        services.AddScoped<RiskRewardCalculator>();
        services.AddScoped<RiskCalculationService>();

        // Register Rule Engine Components
        services.AddScoped<RiskRuleExecutor>();
        services.AddScoped<IRiskRuleEngine, RiskRuleEngine>();

        // Register Risk Rules
        services.AddScoped<IRiskRule, MaxRiskPerTradeRule>();
        services.AddScoped<IRiskRule, MaxOpenPositionsRule>();
        services.AddScoped<IRiskRule, MaximumLeverageRule>();
        services.AddScoped<IRiskRule, MaximumExposureRule>();
        services.AddScoped<IRiskRule, DailyLossRule>();
        services.AddScoped<IRiskRule, DrawdownRule>();
        services.AddScoped<IRiskRule, DuplicatePositionRule>();
        services.AddScoped<IRiskRule, RiskRewardRule>();
        services.AddScoped<IRiskRule, MarginAvailabilityRule>();

        return services;
    }
}
