import { apiGet } from "@/lib/api-client";
import type { ApiSuccess, PagedResult } from "@/types/api";
import type { DashboardOverviewDto } from "@/types/dashboard";
import type { AlertDto, RecentEventDto, HealthHistoryRecordDto, SystemHealthOverviewDto } from "@/types/health";
import type { CompactPerformanceDto, TradingDashboardOverviewDto, TradingOrderDto, TradingPositionDto, TradingTradeDto } from "@/types/trading";

const base = "/api/dashboard";

export interface DateRangeParams {
  from?: string;
  to?: string;
}

export interface PaginationParams {
  page?: number;
  pageSize?: number;
}

function unwrap<T>(response: ApiSuccess<T>): T {
  return response.data;
}

export async function fetchDashboardOverview(token: string) {
  return apiGet<ApiSuccess<DashboardOverviewDto>>(`${base}/overview`, {
    token,
  }).then(unwrap);
}

export async function fetchSystemHealth(
  token: string,
  opts: {
    recentAlertsLimit?: number;
    recentEventsLimit?: number;
    healthHistoryLimit?: number;
  } = {}
) {
  return apiGet<ApiSuccess<SystemHealthOverviewDto>>(`${base}/health`, {
    token,
    query: {
      recentAlertsLimit: opts.recentAlertsLimit ?? 20,
      recentEventsLimit: opts.recentEventsLimit ?? 20,
      healthHistoryLimit: opts.healthHistoryLimit ?? 20,
    },
  }).then(unwrap);
}

export interface TradingQueryParams extends PaginationParams, DateRangeParams {
  symbol?: string;
  side?: string;
  status?: string;
}

export async function fetchTradingOverview(
  token: string,
  params: TradingQueryParams = {}
) {
  return apiGet<ApiSuccess<TradingDashboardOverviewDto>>(`${base}/trading`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchOpenPositions(
  token: string,
  params: TradingQueryParams = {}
) {
  return apiGet<ApiSuccess<PagedResult<TradingPositionDto>>>(
    `${base}/positions`,
    { token, query: params }
  ).then(unwrap);
}

export async function fetchActiveOrders(
  token: string,
  params: TradingQueryParams = {}
) {
  return apiGet<ApiSuccess<PagedResult<TradingOrderDto>>>(`${base}/orders`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchRecentTrades(
  token: string,
  params: TradingQueryParams = {}
) {
  return apiGet<ApiSuccess<PagedResult<TradingTradeDto>>>(`${base}/trades`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchTradingPerformance(
  token: string,
  params: DateRangeParams & { symbol?: string; side?: string } = {}
) {
  return apiGet<ApiSuccess<CompactPerformanceDto>>(`${base}/performance`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchAlerts(
  token: string,
  params: PaginationParams & {
    severity?: string;
    source?: string;
    type?: string;
  } = {}
) {
  return apiGet<ApiSuccess<PagedResult<AlertDto>>>(`${base}/alerts`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchEvents(
  token: string,
  params: PaginationParams &
    DateRangeParams & { type?: string; severity?: string; source?: string } = {}
) {
  return apiGet<ApiSuccess<PagedResult<RecentEventDto>>>(`${base}/events`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchHealthHistory(
  token: string,
  params: PaginationParams &
    DateRangeParams & { service?: string } = {}
) {
  return apiGet<ApiSuccess<PagedResult<HealthHistoryRecordDto>>>(
    `${base}/health/history`,
    { token, query: params }
  ).then(unwrap);
}
