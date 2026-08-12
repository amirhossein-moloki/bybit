"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { ArrowRight, Bell, Wallet } from "lucide-react";
import { useAuth } from "@/lib/auth";
import { useAuthedQuery } from "@/hooks/use-authed-query";
import { PageHeader } from "@/components/shared/page-header";
import { StatCard } from "@/components/shared/stat-card";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { HealthBadge } from "@/components/shared/health-badge";
import { SeverityBadge } from "@/components/shared/severity-badge";
import { PnlText } from "@/components/shared/pnl-text";
import { QueryPanel } from "@/components/shared/query-panel";
import { EmptyState } from "@/components/shared/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { EquityCurveChart } from "@/components/charts/equity-curve-chart";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { SideBadge } from "@/components/shared/side-badge";
import {
  fetchDashboardOverview,
  fetchTradingOverview,
  fetchSystemHealth,
} from "@/services/dashboard-service";
import { fetchEquityCurve } from "@/services/analytics-service";
import {
  formatCurrency,
  formatPercent,
  formatRelativeTime,
  formatDateTime,
} from "@/lib/formatters";

const overviewKey = ["dashboard", "overview"];
const tradingKey = ["dashboard", "trading", "overview", "page", 1, 5];
const healthKey = ["dashboard", "health", 5, 5, 5];
const equityKey = ["analytics", "equity-curve"];

export default function OverviewPage() {
  const { token } = useAuth();

  const overview = useAuthedQuery(overviewKey, (t) => fetchDashboardOverview(t), {
    refetchInterval: 15_000,
  });

  const trading = useAuthedQuery(tradingKey, (t) =>
    fetchTradingOverview(t, { page: 1, pageSize: 5 })
  , { refetchInterval: 10_000 });

  const health = useAuthedQuery(healthKey, (t) =>
    fetchSystemHealth(t, { recentAlertsLimit: 5, recentEventsLimit: 5, healthHistoryLimit: 5 })
  , { refetchInterval: 15_000 });

  const equity = useAuthedQuery(equityKey, (t) => fetchEquityCurve(t), {
    staleTime: 60_000,
  });

  const account = overview.data?.account;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Overview"
        description="Real-time summary of accounts, exposure, PnL and system state."
      />

      {/* Account & PnL */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <StatCard
          label="Equity"
          value={account?.equity !== null ? formatCurrency(account?.equity) : "N/A"}
          loading={overview.isLoading}
          hint="Total account equity. Not populated because the exchange account query returns no value when there are no open positions."
        />
        <StatCard
          label="Balance"
          value={account?.balance !== null ? formatCurrency(account?.balance) : "N/A"}
          loading={overview.isLoading}
        />
        <StatCard
          label="Available Balance"
          value={account?.availableBalance !== null ? formatCurrency(account?.availableBalance) : "N/A"}
          loading={overview.isLoading}
        />
        <StatCard
          label="Used Margin"
          value={formatCurrency(account?.usedMargin)}
          loading={overview.isLoading}
          accent={account?.usedMargin && account.usedMargin > 0 ? "primary" : "neutral"}
        />
        <StatCard
          label="Unrealized PnL"
          value={
            <PnlText value={account?.unrealizedPnL} className="text-2xl" />
          }
          loading={overview.isLoading}
          accent={!account?.unrealizedPnL ? "neutral" : account.unrealizedPnL >= 0 ? "profit" : "loss"}
        />
      </div>

      {/* PnL + Performance summary */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Realized PnL"
          value={
            <PnlText value={overview.data?.pnl.realizedPnL} className="text-2xl" />
          }
          loading={overview.isLoading}
          accent="neutral"
        />
        <StatCard
          label="Total Fees"
          value={formatCurrency(overview.data?.pnl.totalFees)}
          loading={overview.isLoading}
        />
        <StatCard
          label="Net PnL"
          value={
            <PnlText value={overview.data?.pnl.netPnL} className="text-2xl" />
          }
          loading={overview.isLoading}
          accent="neutral"
        />
        <StatCard
          label="Win Rate"
          value={formatPercent(trading.data?.trades.winRate)}
          loading={trading.isLoading}
          sublabel={`${trading.data?.trades.winningTrades ?? "—"}W / ${trading.data?.trades.losingTrades ?? "—"}L / ${trading.data?.trades.breakEvenTrades ?? "—"}BE`}
        />
      </div>

      {/* Positions + Orders + Trades summary */}
      <div className="grid gap-4 md:grid-cols-3">
        <StatCard
          label="Open Positions"
          value={overview.data?.positions.openPositionCount}
          loading={overview.isLoading}
          sublabel={
            <span className="flex gap-3">
              <span className="text-profit">{overview.data?.positions.longPositionCount ?? 0} Long</span>
              <span className="text-loss">{overview.data?.positions.shortPositionCount ?? 0} Short</span>
            </span>
          }
          hint="Positions in Open, PartiallyClosed or Pending state."
        />
        <StatCard
          label="Active Orders"
          value={overview.data?.orders.openOrders}
          loading={overview.isLoading}
          sublabel={`${overview.data?.orders.totalOrders ?? 0} total · ${overview.data?.orders.filledOrders ?? 0} filled · ${overview.data?.orders.cancelledOrders ?? 0} cancelled`}
        />
        <StatCard
          label="Total Trades"
          value={overview.data?.trades.totalTrades}
          loading={overview.isLoading}
          sublabel={
            <span className="flex gap-3">
              <span className="text-profit">{overview.data?.trades.winningTrades ?? 0} won</span>
              <span className="text-loss">{overview.data?.trades.losingTrades ?? 0} lost</span>
            </span>
          }
        />
      </div>

      {/* Equity curve + Health */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Equity Curve</CardTitle>
              <p className="mt-0.5 text-xs text-muted-foreground">
                Cumulative equity across closed trades
              </p>
            </div>
            <Button asChild variant="ghost" size="sm">
              <Link href="/performance">
                Details <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            </Button>
          </CardHeader>
          <CardContent>
            <QueryPanel result={equity} skeleton={<Skeleton className="h-[280px] w-full" />}>
              <EquityCurveChart data={equity.data ?? []} />
            </QueryPanel>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>System Health</CardTitle>
              <p className="mt-0.5 text-xs text-muted-foreground">
                Current component states
              </p>
            </div>
            <Button asChild variant="ghost" size="sm">
              <Link href="/health">
                Details <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            </Button>
          </CardHeader>
          <CardContent className="space-y-3">
            <QueryPanel result={health} skeleton={<Skeleton className="h-48 w-full" />}>
              <div className="space-y-2.5">
                <HealthRow
                  label="Overall"
                  value={health.data?.overallStatus}
                  loading={health.isLoading}
                />
                <HealthRow
                  label="Database"
                  value={health.data?.database.status}
                  loading={health.isLoading}
                />
                <HealthRow
                  label="Bybit REST"
                  value={health.data?.bybit.rest.status}
                  loading={health.isLoading}
                />
                <HealthRow
                  label="Bybit WebSocket"
                  value={health.data?.bybit.webSocket.status}
                  loading={health.isLoading}
                />
                <HealthRow
                  label="Telegram"
                  value={health.data?.telegram.status}
                  loading={health.isLoading}
                />
                <HealthRow
                  label="Monitoring"
                  value={health.data?.monitoring.monitoringStatus}
                  loading={health.isLoading}
                />
              </div>
            </QueryPanel>
          </CardContent>
        </Card>
      </div>

      {/* Recent trades + Alerts */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Recent Trades</CardTitle>
              <p className="mt-0.5 text-xs text-muted-foreground">
                Latest 5 closed trades
              </p>
            </div>
            <Button asChild variant="ghost" size="sm">
              <Link href="/trades">
                View all <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            </Button>
          </CardHeader>
          <CardContent>
            <QueryPanel
              result={trading}
              skeleton={<Skeleton className="h-56 w-full" />}
            >
              {trading.data && trading.data.recentTrades.items.length > 0 ? (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Symbol</TableHead>
                      <TableHead>Side</TableHead>
                      <TableHead>Entry</TableHead>
                      <TableHead>Exit</TableHead>
                      <TableHead className="text-right">Qty</TableHead>
                      <TableHead className="text-right">Net PnL</TableHead>
                      <TableHead className="text-right">Closed</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {trading.data.recentTrades.items.map((trade) => (
                      <TableRow key={trade.id}>
                        <TableCell className="font-medium">{trade.symbol}</TableCell>
                        <TableCell>
                          <SideBadge side={trade.side} />
                        </TableCell>
                        <TableCell className="tabular-nums">
                          {formatCurrency(trade.entryPrice)}
                        </TableCell>
                        <TableCell className="tabular-nums">
                          {formatCurrency(trade.exitPrice)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {trade.quantity}
                        </TableCell>
                        <TableCell className="text-right">
                          <PnlText value={trade.netPnL} />
                        </TableCell>
                        <TableCell className="text-right text-xs text-muted-foreground">
                          {formatRelativeTime(trade.closedAt)}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              ) : (
                <EmptyState
                  title="No trades yet"
                  description="Closed trades will appear here once positions are realized."
                />
              )}
            </QueryPanel>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div className="flex items-center gap-2">
              <Bell className="h-4 w-4 text-muted-foreground" />
              <CardTitle>Active Alerts</CardTitle>
            </div>
            <Button asChild variant="ghost" size="sm">
              <Link href="/alerts">
                View all <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            </Button>
          </CardHeader>
          <CardContent>
            <QueryPanel result={health} skeleton={<Skeleton className="h-56 w-full" />}>
              {health.data && health.data.activeAlerts.length > 0 ? (
                <div className="space-y-2">
                  {health.data.activeAlerts.map((alert) => (
                    <div
                      key={alert.id}
                      className="flex items-start gap-2.5 rounded-md border border-border bg-muted/20 px-3 py-2"
                    >
                      <SeverityBadge severity={alert.severity} />
                      <div className="min-w-0">
                        <p className="line-clamp-2 text-xs text-foreground">
                          {alert.message}
                        </p>
                        <p className="mt-0.5 text-[11px] text-muted-foreground">
                          {alert.source} · {formatRelativeTime(alert.triggeredAt)}
                        </p>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <EmptyState
                  icon={Bell}
                  title="No active alerts"
                  description="The system is currently operating without alerts."
                />
              )}
            </QueryPanel>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function HealthRow({
  label,
  value,
  loading,
}: {
  label: string;
  value?: string;
  loading: boolean;
}) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-sm text-muted-foreground">{label}</span>
      {loading ? (
        <Skeleton className="h-5 w-20" />
      ) : (
        <HealthBadge value={value} />
      )}
    </div>
  );
}
