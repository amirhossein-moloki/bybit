using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Infrastructure.Security;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using TradingBot.Infrastructure.Health;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Persistence.SignalIntelligence.Repositories;
using TradingBot.Infrastructure.Resilience;
using TradingBot.Persistence;
using TradingBot.Persistence.Context;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Application.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind and register Settings
        var settings = new TradingBotSettings();
        configuration.Bind(settings);
        services.AddSingleton(settings);

        // Bind sub-sections for individual IOptions if needed
        services.Configure<ApplicationSettings>(configuration.GetSection("Application"));
        services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
        services.Configure<ExchangeSettings>(configuration.GetSection("Exchange"));
        services.Configure<LoggingSettings>(configuration.GetSection("Logging"));
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));
        services.Configure<ExecutionSettings>(configuration.GetSection("Execution"));

        // Register DbContext (delegated to Persistence layer registration)
        services.AddPersistence(configuration);

        // Register Stage 01 & Stage 02 Trading Execution Services
        services.AddSingleton<TradingBot.Application.Trading.Execution.Contracts.IExchangeInstrumentRules, TradingBot.Application.Trading.Execution.Services.TestExchangeInstrumentRules>();
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.IOrderValidator, TradingBot.Application.Trading.Execution.Services.OrderValidator>();
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.IOrderBuilder, TradingBot.Application.Trading.Execution.Services.OrderBuilder>();
        services.TryAddScoped<TradingBot.Application.Trading.Execution.Contracts.IExchangeTradingGateway, TradingBot.Application.Trading.Execution.Services.TestExchangeTradingGateway>();
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.ITradeExecutionService, TradingBot.Application.Trading.Execution.Services.TradingExecutionService>();

        // Orchestrator, Events, and Observability registrations (Stage 05)
        services.AddSingleton<TradingBot.Application.Trading.Execution.Contracts.IExecutionMetrics, TradingBot.Application.Trading.Execution.Services.ExecutionMetrics>();
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.IExecutionEventHandler, TradingBot.Application.Trading.Execution.Services.ExecutionEventHandler>();
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.IExecutionEventPublisher, TradingBot.Application.Trading.Execution.Services.ExecutionEventPublisher>();
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.ITradeExecutionOrchestrator, TradingBot.Application.Trading.Execution.Services.TradeExecutionOrchestrator>();

        // Register Encryption Service
        services.AddSingleton<IEncryptionService, EncryptionService>();

        // Bind and register StartupShutdown Configuration Options (Section 28)
        var startupShutdownSection = configuration.GetSection("StartupShutdown");
        var startupShutdownOptions = new TradingBot.Application.Configuration.StartupShutdownOptions();
        startupShutdownSection.Bind(startupShutdownOptions);
        services.AddSingleton(startupShutdownOptions);

        // Register Trading Gate & Incomplete Operation Recovery Service
        services.AddSingleton<ITradingGate, TradingGate>();
        services.AddScoped<IIncompleteOperationRecoveryService, IncompleteOperationRecoveryService>();

        // Bind and register Reliability Configuration Options (Section 5 & 6)
        var reliabilitySection = configuration.GetSection("Reliability");
        var reliabilityOptions = new TradingBot.Application.Configuration.ReliabilityOptions();
        reliabilitySection.Bind(reliabilityOptions);
        reliabilityOptions.Validate();
        services.AddSingleton(reliabilityOptions);

        // Bind and register Idempotency Configuration Options (Section 31)
        var idempotencySection = configuration.GetSection("Idempotency");
        var idempotencyOptions = new TradingBot.Application.Configuration.IdempotencyOptions();
        idempotencySection.Bind(idempotencyOptions);
        services.AddSingleton(idempotencyOptions);

        // Register Reliability Services
        services.AddSingleton<IRetryDelayCalculator, RetryDelayCalculator>();
        services.AddSingleton<IErrorClassifier, ErrorClassifier>();
        services.AddSingleton<IReliabilityService, ReliabilityService>();

        // Register Resilience Service
        services.AddSingleton<IResilienceService, ResilienceService>();

        // Register Repositories and Unit Of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISignalRepository, SignalRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IMessageAnalysisRepository, MessageAnalysisRepository>();
        services.AddScoped<ISignalContextRepository, SignalContextRepository>();
        services.AddScoped<ISignalExtractionRepository, SignalExtractionRepository>();
        services.AddScoped<IMessageProcessingTrackerRepository, MessageProcessingTrackerRepository>();
        services.AddScoped<IFailedMessageAnalysisRepository, FailedMessageAnalysisRepository>();
        services.AddScoped<TradingBot.Application.SignalIntelligence.Validation.ISignalValidationService, TradingBot.Application.SignalIntelligence.Validation.SignalValidationService>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderEventRepository, OrderEventRepository>();
        services.AddScoped<IOrderReconciliationService, OrderReconciliationService>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        services.AddScoped<ITradeOperationRepository, TradeOperationRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IPositionSynchronizationService, PositionSynchronizationService>();
        services.AddScoped<IStopLossManager, StopLossManager>();
        services.AddScoped<ITakeProfitManager, TakeProfitManager>();
        services.AddScoped<IPartialCloseManager, PartialCloseManager>();
        services.AddScoped<IPositionReconciliationService, PositionReconciliationService>();
        services.AddScoped<IPositionRecoveryService, PositionRecoveryService>();
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<ISystemLogRepository, SystemLogRepository>();
        services.AddScoped<IRiskEvaluationRepository, RiskEvaluationRepository>();
        services.AddScoped<IRiskProfileRepository, RiskProfileRepository>();
        services.AddScoped<ITradeDecisionRepository, TradeDecisionRepository>();
        services.AddScoped<TradingBot.Domain.Repositories.IParserTemplateRepository, ParserTemplateRepository>();
        services.AddScoped<IRepository<TradingBot.Domain.Entities.Symbol>, SymbolRepository>();
        services.AddScoped<IMonitoringEventRepository, MonitoringEventRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IAlertEventRepository, AlertEventRepository>();
        services.AddScoped<TradingBot.Application.Interfaces.Persistence.ITelegramSourceRepository, TelegramSourceRepository>();

        // Register Dashboard Query Service (Stage 09-01)
        services.AddScoped<TradingBot.Application.Dashboard.Interfaces.IDashboardQueryService, TradingBot.Persistence.Queries.DashboardQueryService>();
        services.AddScoped<TradingBot.Application.Dashboard.Interfaces.ISystemHealthQueryService, TradingBot.Persistence.Queries.SystemHealthQueryService>();
        services.AddScoped<TradingBot.Application.Dashboard.Interfaces.ITradingDashboardQueryService, TradingBot.Persistence.Queries.TradingDashboardQueryService>();

        // Register Analytics Query Service (Stage 11-01)
        services.AddScoped<TradingBot.Application.Analytics.Interfaces.IAnalyticsQueryService, TradingBot.Persistence.Queries.AnalyticsQueryService>();

        // Register Performance Analytics Services (Stage 11-03)
        services.AddSingleton<TradingBot.Application.Analytics.Services.DrawdownCalculator>();
        services.AddSingleton<TradingBot.Application.Analytics.Services.StreakCalculator>();
        services.AddSingleton<TradingBot.Application.Analytics.Services.PnLCalculator>();
        services.AddScoped<TradingBot.Application.Analytics.Interfaces.IPerformanceAnalyticsQueryService, TradingBot.Persistence.Queries.PerformanceAnalyticsQueryService>();
        services.AddScoped<TradingBot.Application.Analytics.Interfaces.IPerformanceAnalyticsService, TradingBot.Application.Analytics.Services.PerformanceAnalyticsService>();

        // Register Analytics Reporting (Stage 11-04)
        services.Configure<TradingBot.Application.Analytics.Configuration.AnalyticsReportOptions>(configuration.GetSection("AnalyticsReport"));
        services.AddMemoryCache();
        services.AddScoped<IReportScheduleRepository, ReportScheduleRepository>();
        services.AddScoped<TradingBot.Application.Analytics.Interfaces.IAnalyticsReportingQueryService, TradingBot.Persistence.Queries.AnalyticsReportingQueryService>();
        services.AddScoped<TradingBot.Application.Analytics.Services.AnalyticsReportingService>();
        services.AddScoped<TradingBot.Application.Analytics.Interfaces.IAnalyticsReportingService>(sp =>
            new TradingBot.Infrastructure.Analytics.Services.CachedAnalyticsReportingService(
                sp.GetRequiredService<TradingBot.Application.Analytics.Services.AnalyticsReportingService>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TradingBot.Application.Analytics.Configuration.AnalyticsReportOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TradingBot.Infrastructure.Analytics.Services.CachedAnalyticsReportingService>>()
            ));

        // New Position Management layer services (Stage 07-04)
        services.AddScoped<IPnLCalculator, PnLCalculator>();
        services.AddScoped<IBreakEvenManager, BreakEvenManager>();
        services.AddScoped<ITrailingStopManager, TrailingStopManager>();
        services.AddScoped<IPositionCloseManager, PositionCloseManager>();

        // Register Health Checks
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("Database")
            .AddCheck<ExchangeHealthCheck>("Exchange")
            .AddCheck<ExchangeConnectionHealthCheck>("ExchangeConnection")
            .AddCheck<WebSocketHealthCheck>("WebSocket")
            .AddCheck<TradingEngineHealthCheck>("TradingEngine");

        // 1. Bind and register Monitoring Configuration Options
        var monitoringSection = configuration.GetSection("Monitoring");
        var monitoringOptions = new TradingBot.Application.Monitoring.Configuration.MonitoringOptions();
        monitoringSection.Bind(monitoringOptions);
        monitoringOptions.Validate();
        services.AddSingleton(monitoringOptions);

        // Bind and register Notification Options
        var notificationSection = configuration.GetSection("Notification");
        var notificationOptions = new TradingBot.Application.Monitoring.Configuration.NotificationOptions();
        notificationSection.Bind(notificationOptions);

        // Support environment variable overrides
        var envChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
        if (!string.IsNullOrEmpty(envChatId))
        {
            notificationOptions.Telegram.ChatId = envChatId;
        }
        notificationOptions.Validate();
        services.AddSingleton(notificationOptions);

        // Bind and register Alert Options
        var alertSection = configuration.GetSection("Alerts");
        var alertOptions = new TradingBot.Application.Monitoring.Configuration.AlertOptions();
        alertSection.Bind(alertOptions);
        alertOptions.Validate();
        services.AddSingleton(alertOptions);

        // 2. Register Singletons
        services.AddSingleton<TradingBot.Application.Monitoring.IMetricsService, TradingBot.Application.Monitoring.Services.MetricsService>();
        services.AddSingleton<TradingBot.Application.Monitoring.IWorkerHealthRegistry, TradingBot.Application.Monitoring.WorkerHealthRegistry>();
        services.AddSingleton<TradingBot.Application.Monitoring.IHealthStatusProvider, TradingBot.Application.Monitoring.HealthStatusProvider>();
        services.AddSingleton<TradingBot.Application.Monitoring.IEventSanitizer, TradingBot.Application.Monitoring.Services.EventSanitizer>();
        services.AddSingleton<TradingBot.Application.Monitoring.IMonitoringEventQueue, TradingBot.Application.Monitoring.Services.MonitoringEventQueue>();

        // 3. Register Repository
        services.AddScoped<TradingBot.Application.Repositories.IHealthCheckResultRepository, TradingBot.Persistence.Repositories.HealthCheckResultRepository>();
        services.AddScoped<TradingBot.Application.Monitoring.IMonitoringEventPublisher, TradingBot.Application.Monitoring.Services.MonitoringEventPublisher>();
        services.AddScoped<TradingBot.Application.Monitoring.IMonitoringEventReader, TradingBot.Persistence.Repositories.MonitoringEventReader>();
        services.AddScoped<TradingBot.Application.Monitoring.ITelegramMessageBuilder, TradingBot.Application.Monitoring.Services.TelegramMessageBuilder>();
        services.AddScoped<TradingBot.Application.Monitoring.INotificationPolicy, TradingBot.Application.Monitoring.Services.NotificationPolicy>();
        services.AddScoped<TradingBot.Application.Monitoring.INotificationEngine, TradingBot.Application.Monitoring.Services.NotificationEngine>();
        services.AddScoped<TradingBot.Application.Monitoring.IAlertEngine, TradingBot.Application.Monitoring.Services.AlertEngine>();

        // 4. Register Custom Monitoring Checks & Engine
        services.AddScoped<TradingBot.Application.Monitoring.IHealthCheck, TradingBot.Infrastructure.Monitoring.Checks.ApplicationHealthCheck>();
        services.AddScoped<TradingBot.Application.Monitoring.IHealthCheck, TradingBot.Infrastructure.Monitoring.Checks.DatabaseHealthCheck>();
        services.AddScoped<TradingBot.Application.Monitoring.IHealthCheck, TradingBot.Infrastructure.Monitoring.Checks.BybitRestHealthCheck>();
        services.AddScoped<TradingBot.Application.Monitoring.IHealthCheck, TradingBot.Infrastructure.Monitoring.Checks.BybitWebSocketHealthCheck>();
        services.AddScoped<TradingBot.Application.Monitoring.IHealthCheck, TradingBot.Infrastructure.Monitoring.Checks.WorkerHealthCheck>();

        services.AddScoped<TradingBot.Application.Monitoring.IHealthCheckEngine, TradingBot.Application.Monitoring.HealthCheckEngine>();

        // 5. Register additional Execution Event Handler for mapping trading events (observability adapter)
        services.AddScoped<TradingBot.Application.Trading.Execution.Contracts.IExecutionEventHandler, TradingBot.Application.Monitoring.Services.MonitoringExecutionEventHandler>();

        return services;
    }
}
