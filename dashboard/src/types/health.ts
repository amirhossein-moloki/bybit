import type { HealthStatus } from "./enums";

export interface ApplicationStatusDto {
  status: string;
  uptime: string;
  startedAt: string;
  currentTimestamp: string;
  environment: string;
}

export interface DatabaseHealthDto {
  status: string;
  lastCheck: string | null;
  responseTime: number | null;
}

export interface BybitServiceStatusDto {
  status: string;
  lastCheck: string | null;
  responseTime: number | null;
}

export interface BybitWebSocketStatusDto {
  status: string;
  connectedAt: string | null;
  lastEventAt: string | null;
  lastDisconnectAt: string | null;
  reconnectCount: number | null;
}

export interface BybitHealthDto {
  rest: BybitServiceStatusDto;
  webSocket: BybitWebSocketStatusDto;
  authenticationStatus: string;
}

export interface TelegramHealthDto {
  status: string;
  lastCheck: string | null;
  lastSuccessfulOperation: string | null;
  lastFailure: string | null;
}

export interface WorkerStatusDto {
  name: string;
  status: string;
  lastActivityAt: string | null;
  lastSuccessfulExecutionAt: string | null;
  lastFailureAt: string | null;
}

export interface MonitoringStatusDto {
  monitoringStatus: string;
  lastSuccessfulCycle: string | null;
  lastFailure: string | null;
}

export interface AlertSummaryDto {
  activeAlertCount: number;
  criticalAlertCount: number;
  errorAlertCount: number;
  warningAlertCount: number;
  infoAlertCount: number;
}

export interface AlertDto {
  id: string;
  type: string;
  severity: string;
  source: string;
  status: string;
  message: string;
  triggeredAt: string;
  lastUpdatedAt: string | null;
  correlationId: string | null;
}

export interface RecentEventDto {
  id: string;
  type: string;
  severity: string;
  source: string;
  timestamp: string;
  correlationId: string | null;
  message: string;
}

export interface HealthHistoryRecordDto {
  service: string;
  status: string;
  checkedAt: string;
  responseTime: number;
}

export interface OperationalMetricsDto {
  ordersSubmitted: number;
  ordersFilled: number;
  ordersFailed: number;
  messagesReceived: number;
  messagesProcessed: number;
  messagesFailed: number;
  notificationsSent: number;
  notificationsFailed: number;
  errorCount: number;
  warningCount: number;
  apiRequestsCount: number;
}

export interface SystemHealthOverviewDto {
  overallStatus: string;
  application: ApplicationStatusDto;
  database: DatabaseHealthDto;
  bybit: BybitHealthDto;
  telegram: TelegramHealthDto;
  workers: WorkerStatusDto[];
  monitoring: MonitoringStatusDto;
  alertSummary: AlertSummaryDto;
  activeAlerts: AlertDto[];
  recentEvents: RecentEventDto[];
  healthHistory: HealthHistoryRecordDto[];
  metrics: OperationalMetricsDto;
}

export interface HealthStatusProviderDto {
  status: string;
  timestamp: string;
  components: Record<string, HealthStatus>;
}
