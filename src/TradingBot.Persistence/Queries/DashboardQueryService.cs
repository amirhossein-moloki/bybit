using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Queries;

public class DashboardQueryService : IDashboardQueryService
{
    private readonly TradingDbContext _dbContext;
    private readonly IHealthStatusProvider? _healthStatusProvider;
    private readonly IMetricsService? _metricsService;

    public DashboardQueryService(
        TradingDbContext dbContext,
        IHealthStatusProvider? healthStatusProvider = null,
        IMetricsService? metricsService = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _healthStatusProvider = healthStatusProvider;
        _metricsService = metricsService;
    }

    public async Task<DashboardOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. System Section
            string appStatus = "Unknown";
            string uptime = "00:00:00";
            if (_healthStatusProvider != null)
            {
                appStatus = _healthStatusProvider.GetOverallStatus().ToString();
            }
            if (_metricsService != null)
            {
                uptime = _metricsService.GetUptime().ToString();
            }
            string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            var systemDto = new SystemStatusDto(
                ApplicationStatus: appStatus,
                Uptime: uptime,
                Environment: env,
                CurrentTimestamp: DateTime.UtcNow
            );

            // 2. Database Section
            string dbStatus = "Unknown";
            if (_healthStatusProvider != null)
            {
                var dbCheck = _healthStatusProvider.GetComponentStatus("Database");
                if (dbCheck != null)
                {
                    dbStatus = dbCheck.Status.ToString();
                }
            }
            var databaseDto = new DatabaseStatusDto(DatabaseStatus: dbStatus);

            // 3. Exchange Section
            string exchangeStatus = "Unknown";
            string exchangeConnStatus = "Unknown";
            if (_healthStatusProvider != null)
            {
                var restStatus = _healthStatusProvider.GetComponentStatus("Bybit REST") ??
                                 _healthStatusProvider.GetComponentStatus("BybitRest");
                var wsStatus = _healthStatusProvider.GetComponentStatus("Bybit WebSocket") ??
                               _healthStatusProvider.GetComponentStatus("BybitWebSocket");

                if (restStatus != null || wsStatus != null)
                {
                    var restVal = restStatus?.Status ?? HealthStatus.Unknown;
                    var wsVal = wsStatus?.Status ?? HealthStatus.Unknown;

                    if (restVal == HealthStatus.Unhealthy || wsVal == HealthStatus.Unhealthy)
                    {
                        exchangeStatus = "Unhealthy";
                    }
                    else if (restVal == HealthStatus.Degraded || wsVal == HealthStatus.Degraded)
                    {
                        exchangeStatus = "Degraded";
                    }
                    else if (restVal == HealthStatus.Healthy || wsVal == HealthStatus.Healthy)
                    {
                        exchangeStatus = "Healthy";
                    }

                    if (wsStatus != null)
                    {
                        exchangeConnStatus = ParseConnectionStatusFromMetadata(wsStatus.Metadata) ??
                            (wsStatus.Status == HealthStatus.Healthy ? "Connected" : "Disconnected");
                    }
                    else if (restStatus != null)
                    {
                        exchangeConnStatus = restStatus.Status == HealthStatus.Healthy ? "Connected" : "Disconnected";
                    }
                }
            }
            var exchangeDto = new ExchangeStatusDto(
                ExchangeStatus: exchangeStatus,
                ConnectionStatus: exchangeConnStatus
            );

            // 4. Telegram Section
            string tgStatus = "Unknown";
            string tgConnStatus = "Unknown";
            if (_healthStatusProvider != null)
            {
                var telegramStatus = _healthStatusProvider.GetComponentStatus("Telegram");
                if (telegramStatus != null)
                {
                    tgStatus = telegramStatus.Status.ToString();
                    tgConnStatus = ParseConnectionStatusFromMetadata(telegramStatus.Metadata) ??
                        (telegramStatus.Status == HealthStatus.Healthy ? "Connected" : "Disconnected");
                }
            }
            var telegramDto = new TelegramStatusDto(
                TelegramStatus: tgStatus,
                ConnectionStatus: tgConnStatus
            );

            // 5. Orders Summary
            var ordersProj = await _dbContext.Orders
                .AsNoTracking()
                .Select(o => new { o.Status })
                .ToListAsync(cancellationToken);

            var totalOrders = ordersProj.Count;
            var openOrders = ordersProj.Count(o => o.Status != OrderStatus.Filled &&
                                                   o.Status != OrderStatus.Cancelled &&
                                                   o.Status != OrderStatus.Rejected &&
                                                   o.Status != OrderStatus.Failed &&
                                                   o.Status != OrderStatus.Expired &&
                                                   o.Status != OrderStatus.ValidationFailed);
            var filledOrders = ordersProj.Count(o => o.Status == OrderStatus.Filled);
            var cancelledOrders = ordersProj.Count(o => o.Status == OrderStatus.Cancelled);
            var failedOrders = ordersProj.Count(o => o.Status == OrderStatus.Failed ||
                                                     o.Status == OrderStatus.ValidationFailed ||
                                                     o.Status == OrderStatus.Rejected);

            var ordersDto = new OrderSummaryDto(
                TotalOrders: totalOrders,
                OpenOrders: openOrders,
                FilledOrders: filledOrders,
                CancelledOrders: cancelledOrders,
                FailedOrders: failedOrders
            );

            // 6. Positions Summary
            var positionsProj = await _dbContext.Positions
                .AsNoTracking()
                .Select(p => new { p.Status, p.Side, p.Margin, p.UnrealizedPnL })
                .ToListAsync(cancellationToken);

            var openPositions = positionsProj.Where(p => p.Status == PositionStatus.Open ||
                                                         p.Status == PositionStatus.PartiallyClosed ||
                                                         p.Status == PositionStatus.Pending).ToList();

            var openPositionCount = openPositions.Count;
            var longPositionCount = openPositions.Count(p => p.Side == OrderSide.Buy);
            var shortPositionCount = openPositions.Count(p => p.Side == OrderSide.Sell);

            var positionsDto = new PositionSummaryDto(
                OpenPositionCount: openPositionCount,
                LongPositionCount: longPositionCount,
                ShortPositionCount: shortPositionCount
            );

            // 7. Trades & PnL Summary
            var tradesProj = await _dbContext.Trades
                .AsNoTracking()
                .Select(t => new { t.NetPnL, t.ProfitLoss, t.Fee })
                .ToListAsync(cancellationToken);

            var totalTrades = tradesProj.Count;
            var winningTrades = tradesProj.Count(t => (t.NetPnL != null ? t.NetPnL.Value > 0 : (t.ProfitLoss ?? 0m) > 0));
            var losingTrades = tradesProj.Count(t => (t.NetPnL != null ? t.NetPnL.Value < 0 : (t.ProfitLoss ?? 0m) < 0));

            var realizedPnLValue = tradesProj.Sum(t => t.ProfitLoss ?? 0m);
            var totalFeesValue = tradesProj.Sum(t => t.Fee);
            var netPnLValue = tradesProj.Sum(t => t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee);

            var tradesDto = new TradeSummaryDto(
                TotalTrades: totalTrades,
                WinningTrades: winningTrades,
                LosingTrades: losingTrades
            );

            var pnlDto = new PnlSummaryDto(
                RealizedPnL: realizedPnLValue,
                TotalFees: totalFeesValue,
                NetPnL: netPnLValue
            );

            // 8. Account Summary Section
            decimal? usedMargin = openPositionCount > 0 ? openPositions.Sum(p => p.Margin ?? 0m) : null;
            decimal? totalUnrealizedPnL = openPositionCount > 0 ? openPositions.Sum(p => p.UnrealizedPnL) : null;

            var accountDto = new AccountSummaryDto(
                Equity: null,
                Balance: null,
                AvailableBalance: null,
                UsedMargin: usedMargin,
                UnrealizedPnL: totalUnrealizedPnL
            );

            return new DashboardOverviewDto(
                System: systemDto,
                Exchange: exchangeDto,
                Telegram: telegramDto,
                Database: databaseDto,
                Orders: ordersDto,
                Positions: positionsDto,
                Trades: tradesDto,
                Pnl: pnlDto,
                Account: accountDto
            );
        }
        catch (Exception ex) when (ex is not DatabaseException)
        {
            throw new DatabaseException("An error occurred while executing the read-only dashboard queries. See inner exception for details.", ex);
        }
    }

    private static string? ParseConnectionStatusFromMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(metadata, @"""ConnectionStatus""\s*:\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
