using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;

namespace TradingBot.Application.Services;

public class PositionRecoveryService : IPositionRecoveryService
{
    private readonly IPositionReconciliationService _reconciliationService;
    private readonly ILogger<PositionRecoveryService> _logger;

    public PositionRecoveryService(
        IPositionReconciliationService reconciliationService,
        ILogger<PositionRecoveryService> logger)
    {
        _reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecoverPositionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Position Sync Started: Recovery flow initiated on application startup.");

        try
        {
            // Perform full reconciliation pass to synchronize/recover positions from exchange
            await _reconciliationService.ReconcileAsync(cancellationToken);

            _logger.LogInformation("Recovery Completed: All active open positions successfully synchronized and monitored.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Position Recovery Failed: An error occurred during startup recovery flow.");
            throw;
        }
    }
}
