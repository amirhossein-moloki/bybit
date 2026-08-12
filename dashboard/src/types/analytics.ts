import type { CloseReason, OrderSide } from "./enums";

export interface PerformanceMetricsDto {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  breakevenTrades: number;
  winRate: number;
  lossRate: number;
  averageWin: number;
  averageLoss: number;
  largestWin: number;
  largestLoss: number;
  averageTradePnL: number;
  profitFactor: number;
  grossProfit: number;
  grossLoss: number;
  netPnL: number;
}

export interface DrawdownMetricsDto {
  peakEquity: number;
  currentEquity: number;
  drawdown: number;
  maximumDrawdown: number;
  maximumDrawdownPercentage: number;
}

export interface StreakMetricsDto {
  currentWinStreak: number;
  currentLossStreak: number;
  maximumWinStreak: number;
  maximumLossStreak: number;
}

export interface DurationMetricsDto {
  averageDuration: string | null;
  shortestDuration: string | null;
  longestDuration: string | null;
  averageWinningDuration: string | null;
  averageLosingDuration: string | null;
}

export interface SidePerformanceDto {
  trades: number;
  wins: number;
  losses: number;
  winRate: number;
  totalPnL: number;
  averagePnL: number;
}

export interface LongShortPerformanceDto {
  long: SidePerformanceDto;
  short: SidePerformanceDto;
}

export interface TradeStatisticsDto {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  breakevenTrades: number;
  winRate: number;
  lossRate: number;
  grossProfit: number;
  grossLoss: number;
  netPnL: number;
  averagePnL: number;
  averageWin: number;
  averageLoss: number;
  largestWin: number;
  largestLoss: number;
  profitFactor: number;
  averageDuration: string | null;
  shortestDuration: string | null;
  longestDuration: string | null;
  currentWinStreak: number;
  currentLossStreak: number;
  maximumWinStreak: number;
  maximumLossStreak: number;
}

export interface EquityPointDto {
  tradeIndex: number;
  tradeId: string | null;
  closedAt: string;
  netPnL: number;
  cumulativePnL: number;
  equity: number;
  drawdown: number;
  drawdownPercentage: number;
  peakEquity: number;
}

export interface PeriodAggregationDto {
  periodLabel: string;
  periodStart: string;
  periodEnd: string;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRate: number;
  grossProfit: number;
  grossLoss: number;
  netPnL: number;
  totalFees: number;
}

export interface PerformanceReportDto {
  generatedAt: string;
  startDate: string | null;
  endDate: string | null;
  initialBalance: number;
  finalBalance: number;
  metrics: PerformanceMetricsDto;
  drawdown: DrawdownMetricsDto;
  streaks: StreakMetricsDto;
  durations: DurationMetricsDto;
  longShort: LongShortPerformanceDto;
  equityCurve: EquityPointDto[];
  detailedTrades: ReportTradeDto[];
}

export interface ReportTradeDto {
  id: string;
  positionId: string | null;
  symbol: string;
  side: OrderSide;
  entryPrice: number;
  exitPrice: number | null;
  quantity: number;
  profitLoss: number | null;
  fee: number;
  fundingFee: number | null;
  netPnL: number;
  closeReason: CloseReason | null;
  openedAt: string | null;
  closedAt: string | null;
}

export interface ReportScheduleDto {
  id: string | null;
  scheduleName: string;
  cronExpression: string;
  reportType: string;
  emailRecipient: string;
  exportFormat: string;
  isActive: boolean;
}

export interface SymbolPerformanceDto {
  symbol: string;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRate: number;
  netPnL: number;
  grossProfit: number;
  grossLoss: number;
  averagePnL: number;
}

export interface SideSignalPerformanceDto {
  side: OrderSide;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRate: number;
  netPnL: number;
  grossProfit: number;
  grossLoss: number;
  averagePnL: number;
}

export interface PnlSummaryResponse {
  grossProfit: number;
  grossLoss: number;
  netPnL: number;
  averagePnL: number;
  profitFactor: number;
}
