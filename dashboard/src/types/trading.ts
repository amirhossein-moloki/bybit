import type {
  CloseReason,
  OrderSide,
  OrderStatus,
  OrderType,
  PositionStatus,
} from "./enums";

export interface TradingPositionDto {
  id: string;
  symbol: string;
  side: OrderSide;
  quantity: number;
  remainingQuantity: number;
  entryPrice: number;
  currentPrice: number;
  stopLoss: number | null;
  takeProfit: number | null;
  leverage: number | null;
  unrealizedPnL: number;
  openedAt: string;
  updatedAt: string | null;
  status: PositionStatus;
}

export interface TradingOrderDto {
  id: string;
  symbol: string;
  side: OrderSide;
  type: OrderType;
  quantity: number;
  price: number;
  status: OrderStatus;
  createdAt: string;
  updatedAt: string | null;
}

export interface TradingTradeDto {
  id: string;
  positionId: string | null;
  symbol: string;
  side: OrderSide;
  entryPrice: number;
  exitPrice: number | null;
  quantity: number;
  grossPnL: number;
  fee: number;
  netPnL: number;
  closeReason: CloseReason | null;
  openedAt: string | null;
  closedAt: string;
}

export interface TradingOrderSummaryDto {
  totalOrders: number;
  openOrders: number;
  filledOrders: number;
  cancelledOrders: number;
  rejectedOrders: number;
  failedOrders: number;
}

export interface TradingPositionSummaryDto {
  openPositionCount: number;
  longPositionCount: number;
  shortPositionCount: number;
  totalOpenQuantity: number;
  totalUnrealizedPnL: number;
}

export interface TradingTradeSummaryDto {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  breakEvenTrades: number;
  winRate: number;
}

export interface TradingPerformanceSummaryDto {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRate: number;
  grossPnL: number;
  fees: number;
  netPnL: number;
}

export interface TradingPnlSummaryDto {
  grossPnL: number;
  totalFees: number;
  netPnL: number;
}

export interface TradingFeeSummaryDto {
  totalFees: number;
}

export interface TradingDashboardOverviewDto {
  orders: TradingOrderSummaryDto;
  positions: TradingPositionSummaryDto;
  trades: TradingTradeSummaryDto;
  performance: TradingPerformanceSummaryDto;
  pnl: TradingPnlSummaryDto;
  fees: TradingFeeSummaryDto;
  openPositions: PagedResultCompat<TradingPositionDto>;
  activeOrders: PagedResultCompat<TradingOrderDto>;
  recentTrades: PagedResultCompat<TradingTradeDto>;
}

import type { PagedResult } from "./api";

type PagedResultCompat<T> = PagedResult<T>;

export interface CompactPerformanceDto {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  breakEvenTrades: number;
  winRate: number;
  grossPnL: number;
  totalFees: number;
  netPnL: number;
}
