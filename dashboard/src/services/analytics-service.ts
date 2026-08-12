import { apiGet, apiPost } from "@/lib/api-client";
import type { ApiSuccess } from "@/types/api";
import type {
  DrawdownMetricsDto,
  DurationMetricsDto,
  EquityPointDto,
  LongShortPerformanceDto,
  PerformanceMetricsDto,
  PerformanceReportDto,
  PeriodAggregationDto,
  PnlSummaryResponse,
  ReportScheduleDto,
  SideSignalPerformanceDto,
  StreakMetricsDto,
  SymbolPerformanceDto,
  TradeStatisticsDto,
} from "@/types/analytics";
import type { AggregationPeriod } from "@/types/enums";

const base = "/api/analytics";

function unwrap<T>(response: ApiSuccess<T>): T {
  return response.data;
}

export interface AnalyticsQueryParams {
  startDate?: string;
  endDate?: string;
  from?: string;
  to?: string;
  symbol?: string;
  side?: string;
}

export interface ReportQueryParams extends AnalyticsQueryParams {
  minPnL?: number;
  maxPnL?: number;
  closeReason?: string;
  initialBalance?: number;
  bypassCache?: boolean;
}

export async function fetchPerformanceMetrics(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<PerformanceMetricsDto>>(`${base}/performance`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchDrawdown(
  token: string,
  params: AnalyticsQueryParams & { initialBalance?: number } = {}
) {
  return apiGet<ApiSuccess<DrawdownMetricsDto>>(`${base}/drawdown`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchStreaks(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<StreakMetricsDto>>(`${base}/streaks`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchDurations(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<DurationMetricsDto>>(`${base}/duration`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchLongShort(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<LongShortPerformanceDto>>(
    `${base}/side-performance`,
    { token, query: params }
  ).then(unwrap);
}

export async function fetchPerformanceReport(
  token: string,
  params: ReportQueryParams = {}
) {
  return apiGet<ApiSuccess<PerformanceReportDto>>(`${base}/report`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchEquityCurve(
  token: string,
  params: ReportQueryParams = {}
) {
  return apiGet<ApiSuccess<EquityPointDto[]>>(`${base}/equity-curve`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchAggregation(
  token: string,
  params: ReportQueryParams & { period?: AggregationPeriod } = {}
) {
  return apiGet<ApiSuccess<PeriodAggregationDto[]>>(`${base}/aggregation`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchTradeStatistics(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<TradeStatisticsDto>>(`${base}/overview`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchPnlSummary(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<PnlSummaryResponse>>(`${base}/pnl`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchSymbolPerformance(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<SymbolPerformanceDto[]>>(`${base}/symbols`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function fetchSignalPerformance(
  token: string,
  params: AnalyticsQueryParams = {}
) {
  return apiGet<ApiSuccess<SideSignalPerformanceDto[]>>(`${base}/signals`, {
    token,
    query: params,
  }).then(unwrap);
}

export async function exportTradesCsv(
  token: string,
  params: AnalyticsQueryParams & {
    minPnL?: number;
    maxPnL?: number;
    closeReason?: string;
  } = {}
): Promise<string> {
  return apiGet<string>(`${base}/export/csv`, { token, query: params });
}

export async function saveReportSchedule(token: string, schedule: ReportScheduleDto) {
  return apiPost<ApiSuccess<ReportScheduleDto>>(`${base}/schedule`, {
    token,
    body: schedule,
  }).then(unwrap);
}
