using TradingBot.Application.Repositories;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingDashboardOverviewDto(
    TradingOrderSummaryDto Orders,
    TradingPositionSummaryDto Positions,
    TradingTradeSummaryDto Trades,
    TradingPerformanceSummaryDto Performance,
    TradingPnlSummaryDto Pnl,
    TradingFeeSummaryDto Fees,
    PagedResult<TradingPositionDto> OpenPositions,
    PagedResult<TradingOrderDto> ActiveOrders,
    PagedResult<TradingTradeDto> RecentTrades
);
