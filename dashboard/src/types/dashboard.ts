export interface ApiErrorBody {
  status: string;
  error: {
    code: string;
    message: string;
    correlationId?: string;
  };
}

export interface ApiSuccess<T> {
  status: "success";
  data: T;
}

export interface PagedResult<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  items: T[];
}

export interface SystemStatusDto {
  applicationStatus: string;
  uptime: string;
  environment: string;
  currentTimestamp: string;
}

export interface ExchangeStatusDto {
  exchangeStatus: string;
  connectionStatus: string;
}

export interface TelegramStatusDto {
  telegramStatus: string;
  connectionStatus: string;
}

export interface DatabaseStatusDto {
  databaseStatus: string;
}

export interface OrderSummaryDto {
  totalOrders: number;
  openOrders: number;
  filledOrders: number;
  cancelledOrders: number;
  failedOrders: number;
}

export interface PositionSummaryDto {
  openPositionCount: number;
  longPositionCount: number;
  shortPositionCount: number;
}

export interface TradeSummaryDto {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
}

export interface PnlSummaryDto {
  realizedPnL: number;
  totalFees: number;
  netPnL: number;
}

export interface AccountSummaryDto {
  equity: number | null;
  balance: number | null;
  availableBalance: number | null;
  usedMargin: number | null;
  unrealizedPnL: number | null;
}

export interface DashboardOverviewDto {
  system: SystemStatusDto;
  exchange: ExchangeStatusDto;
  telegram: TelegramStatusDto;
  database: DatabaseStatusDto;
  orders: OrderSummaryDto;
  positions: PositionSummaryDto;
  trades: TradeSummaryDto;
  pnl: PnlSummaryDto;
  account: AccountSummaryDto;
}
