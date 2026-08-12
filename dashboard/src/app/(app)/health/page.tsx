"use client";

import { useAuthedQuery } from "@/hooks/use-authed-query";
import { PageHeader } from "@/components/shared/page-header";
import { StatCard } from "@/components/shared/stat-card";
import { HealthBadge } from "@/components/shared/health-badge";
import { SeverityBadge } from "@/components/shared/severity-badge";
import { PanelCard, QueryPanel, TableSkeleton, CardSkeleton } from "@/components/shared/query-panel";
import { EmptyState } from "@/components/shared/empty-state";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Card, CardContent } from "@/components/ui/card";
import { fetchSystemHealth } from "@/services/dashboard-service";
import { formatNumber, formatRelativeTime, formatDateTime, formatTimespan } from "@/lib/formatters";
import { normalizeHealth } from "@/lib/status";

function StatusRow({
  label,
  status,
  detail,
  hint,
}: {
  label: string;
  status: string | null | undefined;
  detail?: string;
  hint?: string;
}) {
  const norm = normalizeHealth(status);
  return (
    <div className="flex items-center justify-between gap-3 py-2.5">
      <div className="min-w-0">
        <p className="text-sm font-medium">{label}</p>
        {detail && (
          <p className="truncate text-xs text-muted-foreground">{detail}</p>
        )}
      </div>
      <div className="flex shrink-0 items-center gap-2">
        {hint && <span className="text-xs tabular-nums text-muted-foreground">{hint}</span>}
        <HealthBadge value={norm} />
      </div>
    </div>
  );
}

function HealthMetricCard({
  label,
  value,
}: {
  label: string;
  value: number | string | null | undefined;
}) {
  return (
    <Card>
      <CardContent className="p-3">
        <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
        <p className="mt-1 text-lg font-semibold tabular-nums">
          {value === null || value === undefined ? "—" : String(value)}
        </p>
      </CardContent>
    </Card>
  );
}

export default function HealthPage() {
  const query = useAuthedQuery(
    ["health", "overview"],
    (t) => fetchSystemHealth(t, { recentAlertsLimit: 10, recentEventsLimit: 10, healthHistoryLimit: 10 }),
    { refetchInterval: 10_000 }
  );

  const data = query.data;

  return (
    <div className="space-y-6">
      <PageHeader
        title="System Health"
        description="Live health of the worker, database, exchange connections and monitoring pipeline."
        actions={<HealthBadge value={data?.overallStatus} className="text-sm" />}
      />

      <QueryPanel
        result={query}
        skeleton={
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <CardSkeleton key={i} />
            ))}
          </div>
        }
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            label="Uptime"
            value={formatTimespan(data?.application.uptime)}
            loading={false}
          />
          <StatCard
            label="Environment"
            value={data?.application.environment ?? "—"}
            loading={false}
          />
          <StatCard
            label="Database Response"
            value={
              data?.database.responseTime !== null && data?.database.responseTime !== undefined
                ? `${formatNumber(data.database.responseTime, 0)}ms`
                : "—"
            }
            loading={false}
          />
          <StatCard
            label="API Requests"
            value={formatNumber(data?.metrics.apiRequestsCount, 0)}
            loading={false}
          />
        </div>

        <div className="grid gap-4 lg:grid-cols-2">
          <PanelCard title="Services" subtitle="Application, database and external integrations.">
            <div className="divide-y divide-border px-4">
              <StatusRow
                label="Application"
                status={data?.application.status}
                detail={`Started ${formatRelativeTime(data?.application.startedAt)}`}
              />
              <StatusRow
                label="Database"
                status={data?.database.status}
                detail={data?.database.lastCheck ? `Last check ${formatRelativeTime(data.database.lastCheck)}` : undefined}
                hint={
                  data?.database.responseTime !== null && data?.database.responseTime !== undefined
                    ? `${formatNumber(data.database.responseTime, 0)}ms`
                    : undefined
                }
              />
              <StatusRow
                label="Bybit REST"
                status={data?.bybit.rest.status}
                detail={data?.bybit.rest.lastCheck ? `Last check ${formatRelativeTime(data.bybit.rest.lastCheck)}` : undefined}
                hint={
                  data?.bybit.rest.responseTime !== null && data?.bybit.rest.responseTime !== undefined
                    ? `${formatNumber(data.bybit.rest.responseTime, 0)}ms`
                    : undefined
                }
              />
              <StatusRow
                label="Bybit WebSocket"
                status={data?.bybit.webSocket.status}
                detail={
                  data?.bybit.webSocket.connectedAt
                    ? `Connected ${formatRelativeTime(data.bybit.webSocket.connectedAt)}`
                    : data?.bybit.webSocket.lastDisconnectAt
                      ? `Disconnected ${formatRelativeTime(data.bybit.webSocket.lastDisconnectAt)}`
                      : undefined
                }
                hint={
                  data?.bybit.webSocket.reconnectCount !== null &&
                  data?.bybit.webSocket.reconnectCount !== undefined
                    ? `${data.bybit.webSocket.reconnectCount} reconnects`
                    : undefined
                }
              />
              <StatusRow
                label="Bybit Authentication"
                status={data?.bybit.authenticationStatus}
              />
              <StatusRow
                label="Telegram"
                status={data?.telegram.status}
                detail={
                  data?.telegram.lastFailure
                    ? `Last failure ${formatRelativeTime(data.telegram.lastFailure)}`
                    : data?.telegram.lastSuccessfulOperation
                      ? `Last success ${formatRelativeTime(data.telegram.lastSuccessfulOperation)}`
                      : undefined
                }
              />
              <StatusRow
                label="Monitoring"
                status={data?.monitoring.monitoringStatus}
                detail={
                  data?.monitoring.lastSuccessfulCycle
                    ? `Last cycle ${formatRelativeTime(data.monitoring.lastSuccessfulCycle)}`
                    : data?.monitoring.lastFailure
                      ? `Last failure ${formatRelativeTime(data.monitoring.lastFailure)}`
                      : undefined
                }
              />
            </div>
          </PanelCard>

          <PanelCard title="Operational Metrics" subtitle="Counters accumulated since process start.">
            <div className="grid grid-cols-2 gap-3 px-4 py-3 sm:grid-cols-3">
              <HealthMetricCard label="Orders Submitted" value={data?.metrics.ordersSubmitted} />
              <HealthMetricCard label="Orders Filled" value={data?.metrics.ordersFilled} />
              <HealthMetricCard label="Orders Failed" value={data?.metrics.ordersFailed} />
              <HealthMetricCard label="Messages Received" value={data?.metrics.messagesReceived} />
              <HealthMetricCard label="Messages Processed" value={data?.metrics.messagesProcessed} />
              <HealthMetricCard label="Messages Failed" value={data?.metrics.messagesFailed} />
              <HealthMetricCard label="Notifications Sent" value={data?.metrics.notificationsSent} />
              <HealthMetricCard label="Notifications Failed" value={data?.metrics.notificationsFailed} />
              <HealthMetricCard label="Errors" value={data?.metrics.errorCount} />
              <HealthMetricCard label="Warnings" value={data?.metrics.warningCount} />
              <HealthMetricCard label="API Requests" value={data?.metrics.apiRequestsCount} />
            </div>
          </PanelCard>
        </div>

        <PanelCard title="Workers" subtitle="Background services powering the trading engine.">
          {(data?.workers.length ?? 0) > 0 ? (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Worker</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="text-right">Last Activity</TableHead>
                    <TableHead className="text-right">Last Success</TableHead>
                    <TableHead className="text-right">Last Failure</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data?.workers.map((w) => (
                    <TableRow key={w.name}>
                      <TableCell className="font-medium">{w.name}</TableCell>
                      <TableCell>
                        <HealthBadge value={w.status} />
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatRelativeTime(w.lastActivityAt)}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatRelativeTime(w.lastSuccessfulExecutionAt)}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatRelativeTime(w.lastFailureAt)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          ) : (
            <EmptyState title="No worker status" />
          )}
        </PanelCard>

        <div className="grid gap-4 lg:grid-cols-2">
          <PanelCard title="Active Alerts" subtitle="Alerts that have not yet been resolved.">
            {(data?.activeAlerts.length ?? 0) > 0 ? (
              <div className="divide-y divide-border">
                {data?.activeAlerts.map((a) => (
                  <div key={a.id} className="flex items-start gap-3 px-4 py-3">
                    <SeverityBadge severity={a.severity} className="mt-0.5 shrink-0" />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm">{a.message}</p>
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        {a.source} · {formatRelativeTime(a.triggeredAt)}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <EmptyState
                title="No active alerts"
                description="The system is running without any unresolved alerts."
              />
            )}
          </PanelCard>

          <PanelCard title="Recent Events" subtitle="Latest monitoring events across the system.">
            {(data?.recentEvents.length ?? 0) > 0 ? (
              <div className="divide-y divide-border">
                {data?.recentEvents.map((e) => (
                  <div key={e.id} className="flex items-start gap-3 px-4 py-3">
                    <SeverityBadge severity={e.severity} className="mt-0.5 shrink-0" />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm">{e.message}</p>
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        {e.type} · {e.source} · {formatRelativeTime(e.timestamp)}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <EmptyState
                title="No recent events"
                description="Monitoring events will appear here as they occur."
              />
            )}
          </PanelCard>
        </div>

        <PanelCard title="Health History" subtitle="Recent service status snapshots.">
          {(data?.healthHistory.length ?? 0) > 0 ? (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Service</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="text-right">Checked</TableHead>
                    <TableHead className="text-right">Response</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data?.healthHistory.map((h) => (
                    <TableRow key={`${h.service}-${h.checkedAt}`}>
                      <TableCell className="font-medium">{h.service}</TableCell>
                      <TableCell>
                        <HealthBadge value={h.status} />
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatDateTime(h.checkedAt)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-xs">
                        {h.responseTime !== null && h.responseTime !== undefined
                          ? `${formatNumber(h.responseTime, 0)}ms`
                          : "—"}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          ) : (
            <EmptyState
              title="No health history"
              description="Health snapshots will appear here once the monitoring service records checks."
            />
          )}
        </PanelCard>
      </QueryPanel>
    </div>
  );
}
