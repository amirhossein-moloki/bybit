using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Persistence.Context;

namespace TradingBot.Worker.Lifecycle;

public class StartupRecoveryManager : IStartupRecoveryManager
{
    private readonly ITradingGate _tradingGate;
    private readonly TradingDbContext _dbContext;
    private readonly IExchangeClient _exchangeClient;
    private readonly IPositionRecoveryService _positionRecoveryService;
    private readonly IOrderReconciliationService _orderReconciliationService;
    private readonly IIncompleteOperationRecoveryService _incompleteOperationRecoveryService;
    private readonly StartupShutdownOptions _options;
    private readonly TradingBotSettings _settings;
    private readonly IMonitoringEventPublisher? _eventPublisher;
    private readonly ILogger<StartupRecoveryManager> _logger;

    public StartupRecoveryManager(
        ITradingGate tradingGate,
        TradingDbContext dbContext,
        IExchangeClient exchangeClient,
        IPositionRecoveryService positionRecoveryService,
        IOrderReconciliationService orderReconciliationService,
        IIncompleteOperationRecoveryService incompleteOperationRecoveryService,
        StartupShutdownOptions options,
        TradingBotSettings settings,
        ILogger<StartupRecoveryManager> logger,
        IMonitoringEventPublisher? eventPublisher = null)
    {
        _tradingGate = tradingGate ?? throw new ArgumentNullException(nameof(tradingGate));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _exchangeClient = exchangeClient ?? throw new ArgumentNullException(nameof(exchangeClient));
        _positionRecoveryService = positionRecoveryService ?? throw new ArgumentNullException(nameof(positionRecoveryService));
        _orderReconciliationService = orderReconciliationService ?? throw new ArgumentNullException(nameof(orderReconciliationService));
        _incompleteOperationRecoveryService = incompleteOperationRecoveryService ?? throw new ArgumentNullException(nameof(incompleteOperationRecoveryService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventPublisher = eventPublisher;
    }

    public async Task RunRecoverySequenceAsync(CancellationToken cancellationToken = default)
    {
        var recoveryId = Guid.NewGuid();
        var correlationId = recoveryId.ToString();

        _logger.LogInformation("StartupRecovery: Initializing Startup Recovery Sequence. RecoveryId={RecoveryId}", recoveryId);
        _tradingGate.SetState(ApplicationState.Starting);

        try
        {
            // 1. Configuration Validation
            _logger.LogInformation("StartupRecovery: Step 1/12 - Validating configurations...");
            ValidateConfigurations();

            // 2. Database Availability
            _logger.LogInformation("StartupRecovery: Step 2/12 - Verifying database availability...");
            await VerifyDatabaseAvailabilityAsync(cancellationToken);

            // 3. Infrastructure Initialization
            _logger.LogInformation("StartupRecovery: Step 3/12 - Initializing infrastructure...");
            _tradingGate.SetState(ApplicationState.Initializing);
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new MonitoringEvent(
                    "ApplicationStarting", "INFO", "Startup", "StartupRecoveryManager", "STARTING",
                    "Application is starting up.", correlationId: correlationId, operationId: recoveryId.ToString()
                ), forceSynchronous: true, cancellationToken: cancellationToken);
            }

            // 4. Bybit Connectivity
            _logger.LogInformation("StartupRecovery: Step 4/12 - Verifying Bybit connectivity...");
            await VerifyExchangeConnectivityAsync(cancellationToken);

            // 5. Exchange Synchronization
            _logger.LogInformation("StartupRecovery: Step 5/12 - Starting Exchange Synchronization...");
            _tradingGate.SetState(ApplicationState.Recovering);
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new MonitoringEvent(
                    "StartupRecoveryStarted", "INFO", "Startup", "StartupRecoveryManager", "RECOVERING",
                    "Startup recovery and state synchronization started.", correlationId: correlationId, operationId: recoveryId.ToString()
                ), forceSynchronous: true, cancellationToken: cancellationToken);
            }

            // 6. Incomplete Operation Recovery
            _logger.LogInformation("StartupRecovery: Step 6/12 - Recovering incomplete operations...");
            await LogAndAuditRecoveryStepAsync(recoveryId, "IncompleteOperationRecovery", "TradeOperation", "N/A", "Unknown", "QueryingExchange", "Reconciling", "Initiating incomplete operation recovery", correlationId);
            await _incompleteOperationRecoveryService.RecoverIncompleteOperationsAsync(cancellationToken);
            await LogAndAuditRecoveryStepAsync(recoveryId, "IncompleteOperationRecovery", "TradeOperation", "N/A", "Reconciling", "Completed", "Synchronized", "Completed incomplete operation recovery", correlationId);

            // 7. Position Synchronization
            _logger.LogInformation("StartupRecovery: Step 7/12 - Synchronizing open positions...");
            await LogAndAuditRecoveryStepAsync(recoveryId, "PositionSynchronization", "Position", "N/A", "Unknown", "Syncing", "Syncing", "Synchronizing open positions with Exchange", correlationId);
            await _positionRecoveryService.RecoverPositionsAsync(cancellationToken);
            await LogAndAuditRecoveryStepAsync(recoveryId, "PositionSynchronization", "Position", "N/A", "Syncing", "Completed", "Synchronized", "Completed open position synchronization", correlationId);

            // 8. Order Synchronization
            _logger.LogInformation("StartupRecovery: Step 8/12 - Synchronizing open orders...");
            await LogAndAuditRecoveryStepAsync(recoveryId, "OrderSynchronization", "Order", "N/A", "Unknown", "Syncing", "Syncing", "Synchronizing pending orders with Exchange", correlationId);
            await _orderReconciliationService.ReconcileAsync(cancellationToken);
            await LogAndAuditRecoveryStepAsync(recoveryId, "OrderSynchronization", "Order", "N/A", "Syncing", "Completed", "Synchronized", "Completed order reconciliation pass", correlationId);

            // 9. Monitoring Ready
            _logger.LogInformation("StartupRecovery: Step 9/12 - Verifying monitoring ready...");
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new MonitoringEvent(
                    "StartupRecoveryCompleted", "INFO", "Startup", "StartupRecoveryManager", "RECOVERED",
                    "Startup recovery and state synchronization completed successfully.", correlationId: correlationId, operationId: recoveryId.ToString()
                ), forceSynchronous: true, cancellationToken: cancellationToken);
            }

            // 10. Workers Ready
            _logger.LogInformation("StartupRecovery: Step 10/12 - Setting Application State to Ready...");
            _tradingGate.SetState(ApplicationState.Ready);
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new MonitoringEvent(
                    "ApplicationReady", "INFO", "Startup", "StartupRecoveryManager", "READY",
                    "Application is fully ready and operational.", correlationId: correlationId, operationId: recoveryId.ToString()
                ), forceSynchronous: true, cancellationToken: cancellationToken);
            }

            // 11. Trading Enabled
            _logger.LogInformation("StartupRecovery: Step 11/12 - Enabling centralized Trading Gate...");
            _tradingGate.EnableTrading();

            _logger.LogInformation("StartupRecovery: Step 12/12 - Startup Recovery Sequence Completed successfully. Trading is now ENABLED.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "StartupRecovery: Critical failure during Startup Recovery Sequence. Trading will remain DISABLED.");
            _tradingGate.SetState(ApplicationState.Failed);
            _tradingGate.DisableTrading();

            if (_eventPublisher != null)
            {
                try
                {
                    await _eventPublisher.PublishAsync(new MonitoringEvent(
                        "StartupRecoveryFailed", "CRITICAL", "Startup", "StartupRecoveryManager", "FAILED",
                        $"Startup recovery failed: {ex.Message}", correlationId: correlationId, operationId: recoveryId.ToString()
                    ), forceSynchronous: true, cancellationToken: cancellationToken);
                }
                catch { }
            }

            throw;
        }
    }

    private void ValidateConfigurations()
    {
        if (_options.RequireDatabase && string.IsNullOrWhiteSpace(_settings.Database.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is missing in configuration.");
        }

        if (_options.RequireExchange)
        {
            if (string.IsNullOrWhiteSpace(_settings.Exchange.ApiKey))
            {
                throw new InvalidOperationException("Exchange API key is missing in configuration.");
            }
            if (string.IsNullOrWhiteSpace(_settings.Exchange.ApiSecret))
            {
                throw new InvalidOperationException("Exchange API secret is missing in configuration.");
            }
        }
    }

    private async Task VerifyDatabaseAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!_options.RequireDatabase) return;

        bool canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            throw new InvalidOperationException("Failed to establish initial database connection.");
        }

        // basic query schema check
        try
        {
            await _dbContext.Symbols.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Database health check failed: Schema validation failed.", ex);
        }
    }

    private async Task VerifyExchangeConnectivityAsync(CancellationToken cancellationToken)
    {
        if (!_options.RequireExchange) return;

        bool isConnected = await _exchangeClient.PingAsync(cancellationToken);
        if (!isConnected)
        {
            throw new InvalidOperationException("Failed to verify exchange connectivity.");
        }
    }

    private async Task LogAndAuditRecoveryStepAsync(
        Guid recoveryId,
        string component,
        string entityType,
        string entityId,
        string previousState,
        string exchangeState,
        string finalState,
        string reason,
        string correlationId)
    {
        _logger.LogInformation(
            "RecoveryAudit: RecoveryId={RecoveryId} | Component={Component} | EntityType={EntityType} | EntityId={EntityId} | PreviousState={PreviousState} | ExchangeState={ExchangeState} | FinalState={FinalState} | Reason={Reason} | CorrelationId={CorrelationId}",
            recoveryId, component, entityType, entityId, previousState, exchangeState, finalState, reason, correlationId);

        await Task.CompletedTask;
    }
}
