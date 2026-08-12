"use client";

import { useState } from "react";
import { Download, CalendarClock, CheckCircle2, AlertCircle } from "lucide-react";
import { useAuth } from "@/lib/auth";
import { useToast } from "@/lib/toast";
import { useAuthedQuery } from "@/hooks/use-authed-query";
import { PageHeader } from "@/components/shared/page-header";
import { StatCard } from "@/components/shared/stat-card";
import { PanelCard, QueryPanel, CardSkeleton } from "@/components/shared/query-panel";
import { FilterBar, DateFilter, TextFilter, SideFilter } from "@/components/shared/filters";
import { PnlText } from "@/components/shared/pnl-text";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useMutation } from "@tanstack/react-query";
import {
  exportTradesCsv,
  saveReportSchedule,
  fetchPerformanceReport,
} from "@/services/analytics-service";
import {
  formatCurrency,
  formatNumber,
  formatPercent,
  formatTimespan,
} from "@/lib/formatters";
import { cn } from "@/lib/utils";

function triggerDownload(csv: string, filename: string) {
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

export default function ReportsPage() {
  const { token } = useAuth();
  const { toast } = useToast();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [symbol, setSymbol] = useState("");
  const [side, setSide] = useState("");
  const [closeReason, setCloseReason] = useState("");

  const [scheduleOpen, setScheduleOpen] = useState(false);
  const [scheduleName, setScheduleName] = useState("");
  const [cronExpression, setCronExpression] = useState("");
  const [reportType, setReportType] = useState("DailySummary");
  const [emailRecipient, setEmailRecipient] = useState("");
  const [exportFormat, setExportFormat] = useState("CSV");
  const [isActive, setIsActive] = useState(true);

  const reportQuery = useAuthedQuery(
    ["analytics", "report", { from, to, symbol, side }],
    (t) =>
      fetchPerformanceReport(t, {
        startDate: from || undefined,
        endDate: to || undefined,
        symbol: symbol || undefined,
        side: side || undefined,
      }),
    { refetchInterval: 60_000 }
  );

  const downloadMutation = useMutation({
    mutationFn: (t: string) =>
      exportTradesCsv(t, {
        startDate: from || undefined,
        endDate: to || undefined,
        symbol: symbol || undefined,
        side: side || undefined,
        closeReason: closeReason || undefined,
      }),
    onSuccess: (csv) => {
      const suffix =
        from || to ? `${from || "start"}-${to || "now"}` : "all";
      triggerDownload(csv, `trades-${suffix}.csv`);
      toast({
        title: "Export ready",
        description: "The CSV export has been downloaded.",
        variant: "success",
      });
    },
    onError: (err: Error) => {
      toast({
        title: "Export failed",
        description: err.message,
        variant: "error",
      });
    },
  });

  const scheduleMutation = useMutation({
    mutationFn: (t: string) =>
      saveReportSchedule(t, {
        id: null,
        scheduleName,
        cronExpression,
        reportType,
        emailRecipient,
        exportFormat,
        isActive,
      }),
    onSuccess: (saved) => {
      setScheduleOpen(false);
      setScheduleName("");
      setCronExpression("");
      setEmailRecipient("");
      toast({
        title: "Schedule created",
        description: `Report schedule "${saved.scheduleName}" is now active.`,
        variant: "success",
      });
    },
    onError: (err: Error) => {
      toast({
        title: "Could not create schedule",
        description: err.message,
        variant: "error",
      });
    },
  });

  const report = reportQuery.data;

  const canSubmitSchedule = scheduleName.trim() && cronExpression.trim() && emailRecipient.trim();

  return (
    <div className="space-y-6">
      <PageHeader
        title="Reports"
        description="Generate performance reports and schedule recurring deliveries."
        actions={
          <Button variant="outline" size="sm" onClick={() => setScheduleOpen(true)}>
            <CalendarClock className="h-4 w-4" />
            Schedule Report
          </Button>
        }
      />

      <FilterBar
        onReset={() => {
          setFrom("");
          setTo("");
          setSymbol("");
          setSide("");
          setCloseReason("");
        }}
      >
        <DateFilter
          label="From"
          value={from}
          onChange={setFrom}
        />
        <DateFilter
          label="To"
          value={to}
          onChange={setTo}
        />
        <TextFilter
          value={symbol}
          onChange={setSymbol}
          placeholder="Filter by symbol…"
        />
        <SideFilter
          value={side}
          onChange={setSide}
        />
      </FilterBar>

      <QueryPanel
        result={reportQuery}
        skeleton={
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <CardSkeleton key={i} />
            ))}
          </div>
        }
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            label="Initial Balance"
            value={formatCurrency(report?.initialBalance)}
            loading={false}
          />
          <StatCard
            label="Final Balance"
            value={formatCurrency(report?.finalBalance)}
            loading={false}
          />
          <StatCard
            label="Net PnL"
            value={
              <PnlText value={report?.metrics.netPnL} className="text-2xl" />
            }
            loading={false}
          />
          <StatCard
            label="Profit Factor"
            value={formatNumber(report?.metrics.profitFactor, 2)}
            loading={false}
          />
        </div>

        <div className="grid gap-4 lg:grid-cols-2">
          <PanelCard title="Performance" subtitle="Aggregate trade metrics for the selected period.">
            <div className="grid grid-cols-2 gap-x-6 gap-y-3 px-4 py-3 sm:grid-cols-3">
              <MetricRow label="Total Trades" value={formatNumber(report?.metrics.totalTrades, 0)} />
              <MetricRow label="Winning" value={formatNumber(report?.metrics.winningTrades, 0)} />
              <MetricRow label="Losing" value={formatNumber(report?.metrics.losingTrades, 0)} />
              <MetricRow label="Win Rate" value={formatPercent(report?.metrics.winRate)} />
              <MetricRow
                label="Gross Profit"
                value={<PnlText value={report?.metrics.grossProfit} />}
              />
              <MetricRow
                label="Gross Loss"
                value={<PnlText value={report?.metrics.grossLoss} />}
              />
              <MetricRow
                label="Avg Win"
                value={<PnlText value={report?.metrics.averageWin} />}
              />
              <MetricRow
                label="Avg Loss"
                value={<PnlText value={report?.metrics.averageLoss} />}
              />
              <MetricRow
                label="Largest Win"
                value={<PnlText value={report?.metrics.largestWin} />}
              />
              <MetricRow
                label="Largest Loss"
                value={<PnlText value={report?.metrics.largestLoss} />}
              />
              <MetricRow
                label="Avg Trade PnL"
                value={<PnlText value={report?.metrics.averageTradePnL} />}
              />
              <MetricRow label="Break-even" value={formatNumber(report?.metrics.breakevenTrades, 0)} />
            </div>
          </PanelCard>

          <div className="grid gap-4">
            <PanelCard title="Drawdown" subtitle="Equity drawdown over the selected period.">
              <div className="grid grid-cols-2 gap-x-6 gap-y-3 px-4 py-3">
                <MetricRow label="Current Drawdown" value={formatCurrency(report?.drawdown.drawdown, { sign: true })} />
                <MetricRow label="Max Drawdown" value={formatCurrency(report?.drawdown.maximumDrawdown, { sign: true })} />
                <MetricRow label="Max Drawdown %" value={formatPercent(report?.drawdown.maximumDrawdownPercentage)} />
                <MetricRow label="Peak Equity" value={formatCurrency(report?.drawdown.peakEquity)} />
              </div>
            </PanelCard>
            <PanelCard title="Streaks & Duration" subtitle="Runs and holding times across trades.">
              <div className="grid grid-cols-2 gap-x-6 gap-y-3 px-4 py-3">
                <MetricRow label="Win Streak" value={formatNumber(report?.streaks.maximumWinStreak, 0)} />
                <MetricRow label="Loss Streak" value={formatNumber(report?.streaks.maximumLossStreak, 0)} />
                <MetricRow label="Avg Duration" value={formatTimespan(report?.durations.averageDuration)} />
                <MetricRow label="Avg Win Duration" value={formatTimespan(report?.durations.averageWinningDuration)} />
              </div>
            </PanelCard>
          </div>
        </div>

        <div className="grid gap-4 lg:grid-cols-2">
          <PanelCard title="Long / Short" subtitle="Breakdown of performance by position side.">
            <div className="grid grid-cols-2 gap-x-6 gap-y-3 px-4 py-3">
              <SideSplit label="Long" data={report?.longShort.long} />
              <SideSplit label="Short" data={report?.longShort.short} />
            </div>
          </PanelCard>

          <PanelCard title="Export Trades" subtitle="Download trade history as CSV with the current filters.">
            <div className="space-y-3 px-4 py-3">
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="closeReason" className="text-xs text-muted-foreground">
                    Close Reason
                  </Label>
                  <Select value={closeReason || undefined} onValueChange={(v) => setCloseReason(v === "all" ? "" : v)}>
                    <SelectTrigger id="closeReason" className="h-9">
                      <SelectValue placeholder="All reasons" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">All reasons</SelectItem>
                      <SelectItem value="StopLoss">Stop Loss</SelectItem>
                      <SelectItem value="TakeProfit">Take Profit</SelectItem>
                      <SelectItem value="Manual">Manual</SelectItem>
                      <SelectItem value="Signal">Signal</SelectItem>
                      <SelectItem value="Liquidation">Liquidation</SelectItem>
                      <SelectItem value="Exchange">Exchange</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="flex items-end">
                  <Button
                    variant="default"
                    size="sm"
                    className="h-9 w-full gap-2"
                    disabled={downloadMutation.isPending}
                    onClick={() => token && downloadMutation.mutate(token)}
                  >
                    {downloadMutation.isPending ? (
                      <span className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
                    ) : (
                      <Download className="h-4 w-4" />
                    )}
                    {downloadMutation.isPending ? "Exporting…" : "Download CSV"}
                  </Button>
                </div>
              </div>
              {downloadMutation.isError && (
                <p className="flex items-center gap-1.5 text-xs text-loss">
                  <AlertCircle className="h-3.5 w-3.5" />
                  {downloadMutation.error?.message}
                </p>
              )}
            </div>
          </PanelCard>
        </div>
      </QueryPanel>

      <Dialog open={scheduleOpen} onOpenChange={setScheduleOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Schedule Report</DialogTitle>
            <DialogDescription>
              Configure a recurring report delivered to an email address. The
              backend cron service will generate and send it on schedule.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label htmlFor="scheduleName">Schedule Name</Label>
              <Input
                id="scheduleName"
                value={scheduleName}
                onChange={(e) => setScheduleName(e.target.value)}
                placeholder="Weekly performance summary"
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="cronExpression">Cron Expression</Label>
              <Input
                id="cronExpression"
                value={cronExpression}
                onChange={(e) => setCronExpression(e.target.value)}
                placeholder="0 8 * * MON"
              />
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="reportType">Report Type</Label>
                <Select value={reportType} onValueChange={setReportType}>
                  <SelectTrigger id="reportType" className="h-9">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="DailySummary">Daily Summary</SelectItem>
                    <SelectItem value="WeeklyPerformance">Weekly Performance</SelectItem>
                    <SelectItem value="MonthlyReport">Monthly Report</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="exportFormat">Format</Label>
                <Select value={exportFormat} onValueChange={setExportFormat}>
                  <SelectTrigger id="exportFormat" className="h-9">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="CSV">CSV</SelectItem>
                    <SelectItem value="JSON">JSON</SelectItem>
                    <SelectItem value="PDF">PDF</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="emailRecipient">Email Recipient</Label>
              <Input
                id="emailRecipient"
                type="email"
                value={emailRecipient}
                onChange={(e) => setEmailRecipient(e.target.value)}
                placeholder="ops@example.com"
              />
            </div>
            <label className="flex cursor-pointer items-center justify-between rounded-md border border-border p-3">
              <div>
                <p className="text-sm font-medium">Active</p>
                <p className="text-xs text-muted-foreground">
                  Pause the schedule without deleting it.
                </p>
              </div>
              <Switch checked={isActive} onCheckedChange={setIsActive} />
            </label>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setScheduleOpen(false)}>
              Cancel
            </Button>
            <Button
              disabled={!canSubmitSchedule || scheduleMutation.isPending}
              onClick={() => token && scheduleMutation.mutate(token)}
            >
              {scheduleMutation.isPending ? (
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
              ) : (
                <CheckCircle2 className="h-4 w-4" />
              )}
              Create Schedule
            </Button>
          </DialogFooter>
          {scheduleMutation.isError && (
            <p className="flex items-center gap-1.5 text-xs text-loss">
              <AlertCircle className="h-3.5 w-3.5" />
              {scheduleMutation.error?.message}
            </p>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}

function MetricRow({
  label,
  value,
}: {
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="flex items-baseline justify-between gap-2">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className={cn("text-sm font-medium tabular-nums")}>{value}</span>
    </div>
  );
}

function SideSplit({
  label,
  data,
}: {
  label: string;
  data?: { trades: number; wins: number; losses: number; winRate: number; totalPnL: number; averagePnL: number };
}) {
  return (
    <div className="rounded-md border border-border p-3">
      <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
        {label}
      </p>
      <dl className="mt-2 space-y-1.5 text-sm">
        <div className="flex justify-between">
          <dt className="text-muted-foreground">Trades</dt>
          <dd className="tabular-nums">{formatNumber(data?.trades, 0)}</dd>
        </div>
        <div className="flex justify-between">
          <dt className="text-muted-foreground">Win Rate</dt>
          <dd className="tabular-nums">{formatPercent(data?.winRate)}</dd>
        </div>
        <div className="flex justify-between">
          <dt className="text-muted-foreground">Total PnL</dt>
          <dd>
            <PnlText value={data?.totalPnL} />
          </dd>
        </div>
        <div className="flex justify-between">
          <dt className="text-muted-foreground">Avg PnL</dt>
          <dd>
            <PnlText value={data?.averagePnL} />
          </dd>
        </div>
      </dl>
    </div>
  );
}
