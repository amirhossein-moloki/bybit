namespace TradingBot.Application.Dashboard.DTOs;

public sealed record DashboardOverviewDto(
    SystemStatusDto System,
    ExchangeStatusDto Exchange,
    TelegramStatusDto Telegram,
    DatabaseStatusDto Database,
    OrderSummaryDto Orders,
    PositionSummaryDto Positions,
    TradeSummaryDto Trades,
    PnlSummaryDto Pnl,
    AccountSummaryDto Account
);
