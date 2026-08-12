"use client";

import { useMemo, useState } from "react";
import { useAuth } from "@/lib/auth";
import { useAuthedQuery } from "@/hooks/use-authed-query";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { PageHeader } from "@/components/shared/page-header";
import { StatCard } from "@/components/shared/stat-card";
import { PanelCard, QueryPanel } from "@/components/shared/query-panel";
import { PnlText } from "@/components/shared/pnl-text";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/shared/empty-state";
import { EquityCurveChart } from "@/components/charts/equity-curve-chart";
import { PnlBarChart } from "@/components/charts/pnl-bar-chart";
import { DonutChart } from "@/components/charts/donut-chart";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  fetchPerformanceMetrics,
  fetchDrawdown,
  fetchStreaks,
  fetchDurations,
  fetchLongShort,
  fetchEquityCurve,
  fetchAggregation,
  fetchSymbolPerformance,
} from "@/services/analytics-service";
import { formatCurrency, formatPercent, formatTimespan } from "@/lib/formatters";
import { chartColors } from "@/components/charts/chart-colors";
import type { AggregationPeriod } from "@/types/enums";
import { Input } from "@/components/ui/input";

const presets: Array<{ label: string; days: number | null }> = [
  { label: "All time", days: null },
  { label: "Last 7 days", days: 7 },
  { label: "Last 30 days", days: 30 },
  { label: "Last 90 days", days: 90 },
  { label: "Last year", days: 365 },
];

function toIsoDate(daysAgo: number): string {
  const d = new Date();
  d.setDate(d.getDate() - daysAgo);
  return d.toISOString();
}

export default function PerformancePage() {
  const { token } = useAuth();
  const [preset, setPreset] = useState<string>("all");
  const [symbol, setSymbol] = useState("");
  const [period, setPeriod] = useState<AggregationPeriod>("Daily");
  const debouncedSymbol = useDebouncedValue(symbol);

  const { startDate, endDate } = useMemo(() => {
    const days = presets.find((p) => p.label === preset)?.days ?? null;
    return {
      startDate: days !== null ? toIsoDate(days) : undefined,
      endDate: undefined,
    };
  }, [preset]);

  const params = {
    startDate,
    endDate,
    symbol: debouncedSymbol || undefined,
  };

  const metrics = useAuthedQuery(
    ["analytics", "performance", params],
    (t) => fetchPerformanceMetrics(t, params)
  );

  const drawdown = useAuthedQuery(
    ["analytics", "drawdown", params],
    (t) => fetchDrawdown(t, params)
  );

  const streaks = useAuthedQuery(
    ["analytics", "streaks", params],
    (t) => fetchStreaks(t, params)
  );

  const durations = useAuthedQuery(
    ["analytics", "duration", params],
    (t) => fetchDurations(t, params)
  );

  const longShort = useAuthedQuery(
    ["analytics", "side-performance", params],
    (t) => fetchLongShort(t, params)
  );

  const equity = useAuthedQuery(
    ["analytics", "equity-curve", params],
    (t) => fetchEquityCurve(t, params)
  );

  const aggregation = useAuthedQuery(
    ["analytics", "aggregation", params, period],
    (t) => fetchAggregation(t, { ...params, period })
  );

  const symbols = useAuthedQuery(
    ["analytics", "symbols", params],
    (t) => fetchSymbolPerformance(t, params)
  );

  const aggData = useMemo(
    () =>
      (aggregation.data ?? []).map((a) => ({
        label: a.periodLabel,
        netPnL: a.netPnL,
      })),
    [aggregation.data]
  );

  const loading = metrics.isLoading;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Performance"
        description="Analytics over closed trades, computed by the backend analytics engine."
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Input
              value={symbol}
              onChange={(e) => setSymbol(e.target.value)}
              placeholder="Symbol filter"
              className="h-9 w-40"
            />
            <Select value={preset} onValueChange={setPreset}>
              <SelectTrigger className="h-9 w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {presets.map((p) => (
                  <SelectItem key={p.label} value={p.label}>
                    {p.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        }
      />

      {/* PnL + trade metrics */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Total Trades"
          value={metrics.data?.totalTrades}
          loading={loading}
        />
        <StatCard
          label="Net PnL"
          value={<PnlText value={metrics.data?.netPnL} className="text-2xl" />}
          loading={loading}
        />
        <StatCard
          label="Profit Factor"
          value={metrics.data?.profitFactor !== undefined ? metrics.data.profitFactor.toFixed(2) : "—"}
          loading={loading}
          accent={
            metrics.data && metrics.data.profitFactor >= 1 ? "profit" : "neutral"
          }
        />
        <StatCard
          label="Win Rate"
          value={formatPercent(metrics.data?.winRate)}
          loading={loading}
          sublabel={
            metrics.data
              ? `${metrics.data.winningTrades}W · ${metrics.data.losingTrades}L · ${metrics.data.breakevenTrades}BE`
              : undefined
          }
        />
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Gross Profit"
          value={<PnlText value={metrics.data?.grossProfit} className="text-xl" />}
          loading={loading}
        />
        <StatCard
          label="Gross Loss"
          value={<PnlText value={metrics.data?.grossLoss} className="text-xl" />}
          loading={loading}
        />
        <StatCard
          label="Avg. Trade PnL"
          value={<PnlText value={metrics.data?.averageTradePnL} className="text-xl" />}
          loading={loading}
        />
        <StatCard
          label="Avg. Win / Loss"
          value={
            metrics.data ? (
              <span className="text-sm font-medium">
                <span className="text-profit">
                  {formatCurrency(metrics.data.averageWin)}
                </span>
                {" / "}
                <span className="text-loss">
                  {formatCurrency(metrics.data.averageLoss)}
                </span>
              </span>
            ) : (
              "—"
            )
          }
          loading={loading}
        />
      </div>

      {/* Drawdown + streaks + duration */}
      <div className="grid gap-4 md:grid-cols-3">
        <PanelCard title="Drawdown" subtitle="From the drawdown calculator">
          <QueryPanel result={drawdown} skeleton={<Skeleton className="h-40 w-full" />}>
            {drawdown.data ? (
              <div className="space-y-2.5 p-4">
                <StatLine label="Peak Equity" value={formatCurrency(drawdown.data.peakEquity)} />
                <StatLine label="Current Equity" value={formatCurrency(drawdown.data.currentEquity)} />
                <StatLine
                  label="Drawdown"
                  value={<PnlText value={drawdown.data.drawdown} className="text-sm" />}
                />
                <StatLine
                  label="Max Drawdown"
                  value={<PnlText value={drawdown.data.maximumDrawdown} className="text-sm" />}
                />
                <StatLine
                  label="Max Drawdown %"
                  value={formatPercent(drawdown.data.maximumDrawdownPercentage)}
                />
              </div>
            ) : null}
          </QueryPanel>
        </PanelCard>

        <PanelCard title="Streaks" subtitle="Winning and losing streaks">
          <QueryPanel result={streaks} skeleton={<Skeleton className="h-40 w-full" />}>
            {streaks.data ? (
              <div className="space-y-2.5 p-4">
                <StatLine
                  label="Current Win Streak"
                  value={<span className="text-profit">{streaks.data.currentWinStreak}</span>}
                />
                <StatLine
                  label="Current Loss Streak"
                  value={<span className="text-loss">{streaks.data.currentLossStreak}</span>}
                />
                <StatLine
                  label="Max Win Streak"
                  value={<span className="text-profit">{streaks.data.maximumWinStreak}</span>}
                />
                <StatLine
                  label="Max Loss Streak"
                  value={<span className="text-loss">{streaks.data.maximumLossStreak}</span>}
                />
              </div>
            ) : null}
          </QueryPanel>
        </PanelCard>

        <PanelCard title="Duration" subtitle="Time in trade">
          <QueryPanel result={durations} skeleton={<Skeleton className="h-40 w-full" />}>
            {durations.data ? (
              <div className="space-y-2.5 p-4">
                <StatLine
                  label="Average Duration"
                  value={formatTimespan(durations.data.averageDuration)}
                />
                <StatLine
                  label="Shortest"
                  value={formatTimespan(durations.data.shortestDuration)}
                />
                <StatLine
                  label="Longest"
                  value={formatTimespan(durations.data.longestDuration)}
                />
                <StatLine
                  label="Avg Winning"
                  value={formatTimespan(durations.data.averageWinningDuration)}
                />
                <StatLine
                  label="Avg Losing"
                  value={formatTimespan(durations.data.averageLosingDuration)}
                />
              </div>
            ) : null}
          </QueryPanel>
        </PanelCard>
      </div>

      {/* Equity + Long/Short + Aggregation */}
      <div className="grid gap-4 lg:grid-cols-3">
        <PanelCard
          title="Equity Curve"
          subtitle="Cumulative equity over closed trades"
          className="lg:col-span-2"
        >
          <QueryPanel result={equity} skeleton={<Skeleton className="h-[280px] w-full" />}>
            <div className="p-4 pt-2">
              <EquityCurveChart data={equity.data ?? []} />
            </div>
          </QueryPanel>
        </PanelCard>

        <PanelCard title="Long vs Short" subtitle="Performance by direction">
          <QueryPanel result={longShort} skeleton={<Skeleton className="h-[280px] w-full" />}>
            {longShort.data ? (
              <div className="p-4 pt-2">
                <DonutChart
                  height={160}
                  centerLabel="Trades"
                  centerValue={String(
                    (longShort.data.long.trades ?? 0) + (longShort.data.short.trades ?? 0)
                  )}
                  data={[
                    {
                      name: "Long",
                      value: longShort.data.long.trades,
                      color: chartColors.profit,
                    },
                    {
                      name: "Short",
                      value: longShort.data.short.trades,
                      color: chartColors.loss,
                    },
                  ]}
                />
                <div className="mt-3 space-y-2">
                  <SideRow
                    label="Long PnL"
                    value={<PnlText value={longShort.data.long.totalPnL} className="text-sm" />}
                    winRate={longShort.data.long.winRate}
                  />
                  <SideRow
                    label="Short PnL"
                    value={<PnlText value={longShort.data.short.totalPnL} className="text-sm" />}
                    winRate={longShort.data.short.winRate}
                  />
                </div>
              </div>
            ) : null}
          </QueryPanel>
        </PanelCard>
      </div>

      {/* Aggregation tab */}
      <PanelCard
        title="PnL Aggregation"
        subtitle="Net PnL grouped by period"
        contentClassName="p-4"
      >
        <div className="mb-3 flex items-center justify-end">
          <Tabs value={period} onValueChange={(v) => setPeriod(v as AggregationPeriod)}>
            <TabsList>
              <TabsTrigger value="Daily">Daily</TabsTrigger>
              <TabsTrigger value="Weekly">Weekly</TabsTrigger>
              <TabsTrigger value="Monthly">Monthly</TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
        <QueryPanel result={aggregation} skeleton={<Skeleton className="h-[280px] w-full" />}>
          <PnlBarChart data={aggData} />
        </QueryPanel>
      </PanelCard>

      {/* Symbol performance */}
      <PanelCard
        title="Symbol Performance"
        subtitle="Per-symbol breakdown computed by the backend"
      >
        <QueryPanel result={symbols} skeleton={<Skeleton className="h-48 w-full" />}>
          {symbols.data && symbols.data.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Symbol</TableHead>
                  <TableHead className="text-right">Trades</TableHead>
                  <TableHead className="text-right">Wins</TableHead>
                  <TableHead className="text-right">Losses</TableHead>
                  <TableHead className="text-right">Win Rate</TableHead>
                  <TableHead className="text-right">Gross Profit</TableHead>
                  <TableHead className="text-right">Gross Loss</TableHead>
                  <TableHead className="text-right">Net PnL</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {symbols.data.map((s) => (
                  <TableRow key={s.symbol}>
                    <TableCell className="font-medium">{s.symbol}</TableCell>
                    <TableCell className="text-right tabular-nums">{s.totalTrades}</TableCell>
                    <TableCell className="text-right tabular-nums text-profit">{s.winningTrades}</TableCell>
                    <TableCell className="text-right tabular-nums text-loss">{s.losingTrades}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatPercent(s.winRate)}</TableCell>
                    <TableCell className="text-right">
                      <PnlText value={s.grossProfit} />
                    </TableCell>
                    <TableCell className="text-right">
                      <PnlText value={-s.grossLoss} />
                    </TableCell>
                    <TableCell className="text-right">
                      <PnlText value={s.netPnL} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <EmptyState
              title="No symbol data"
              description="No completed trades in the selected range."
            />
          )}
        </QueryPanel>
      </PanelCard>
    </div>
  );
}

function StatLine({
  label,
  value,
}: {
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium tabular-nums">{value}</span>
    </div>
  );
}

function SideRow({
  label,
  value,
  winRate,
}: {
  label: string;
  value: React.ReactNode;
  winRate: number;
}) {
  return (
    <div className="flex items-center justify-between rounded-md bg-muted/40 px-3 py-2">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="flex items-center gap-3 text-sm">
        <span className="text-[11px] text-muted-foreground">
          {formatPercent(winRate)} win
        </span>
        {value}
      </span>
    </div>
  );
}
