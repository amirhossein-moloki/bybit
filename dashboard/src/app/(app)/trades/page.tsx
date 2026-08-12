"use client";

import { useState } from "react";
import { useAuth } from "@/lib/auth";
import { useAuthedQuery } from "@/hooks/use-authed-query";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { PageHeader } from "@/components/shared/page-header";
import { StatCard } from "@/components/shared/stat-card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { SideBadge } from "@/components/shared/side-badge";
import { PnlText } from "@/components/shared/pnl-text";
import { Pagination } from "@/components/shared/pagination";
import { QueryPanel, TableSkeleton } from "@/components/shared/query-panel";
import { EmptyState } from "@/components/shared/empty-state";
import {
  FilterBar,
  DateFilter,
  SideFilter,
  TextFilter,
} from "@/components/shared/filters";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { fetchRecentTrades, fetchTradingOverview } from "@/services/dashboard-service";
import {
  formatCurrency,
  formatDateTime,
  formatDuration,
  formatNumber,
  formatRelativeTime,
} from "@/lib/formatters";
import type { TradingTradeDto } from "@/types/trading";

const closeReasonVariant: Record<string, "success" | "warning" | "muted" | "destructive" | "secondary"> = {
  TakeProfit: "success",
  StopLoss: "destructive",
  Manual: "secondary",
  Signal: "secondary",
  Liquidation: "destructive",
  Exchange: "warning",
};

export default function TradesPage() {
  const { token } = useAuth();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [symbol, setSymbol] = useState("");
  const [side, setSide] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [selected, setSelected] = useState<TradingTradeDto | null>(null);
  const debouncedSymbol = useDebouncedValue(symbol);

  const query = useAuthedQuery(
    ["trades", { page, pageSize, symbol: debouncedSymbol, side, from, to }],
    (t) =>
      fetchRecentTrades(t, {
        page,
        pageSize,
        symbol: debouncedSymbol || undefined,
        side: side || undefined,
        from: from || undefined,
        to: to || undefined,
      }),
    { placeholderData: (prev) => prev }
  );

  const summary = useAuthedQuery(
    ["trading", "summary", { symbol: debouncedSymbol, from, to }],
    (t) =>
      fetchTradingOverview(t, {
        symbol: debouncedSymbol || undefined,
        from: from || undefined,
        to: to || undefined,
      }),
    { refetchInterval: 15_000 }
  );

  const data = query.data;
  const items = data?.items ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Trade History"
        description="Realized trades with entry, exit, PnL, fees and duration."
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Total Trades" value={summary.data?.trades.totalTrades} loading={summary.isLoading} />
        <StatCard
          label="Winning / Losing"
          value={
            <span className="flex items-center gap-2">
              <span className="text-profit">{summary.data?.trades.winningTrades ?? 0}</span>
              <span className="text-muted-foreground">/</span>
              <span className="text-loss">{summary.data?.trades.losingTrades ?? 0}</span>
            </span>
          }
          loading={summary.isLoading}
        />
        <StatCard label="Break Even" value={summary.data?.trades.breakEvenTrades} loading={summary.isLoading} />
        <StatCard
          label="Win Rate"
          value={summary.data ? `${summary.data.trades.winRate.toFixed(1)}%` : "—"}
          loading={summary.isLoading}
        />
      </div>

      <FilterBar
        onReset={() => {
          setSymbol("");
          setSide("");
          setFrom("");
          setTo("");
          setPage(1);
        }}
      >
        <TextFilter
          value={symbol}
          onChange={(v) => {
            setSymbol(v);
            setPage(1);
          }}
          placeholder="Filter by symbol…"
        />
        <SideFilter
          value={side}
          onChange={(v) => {
            setSide(v);
            setPage(1);
          }}
        />
        <DateFilter
          label="From"
          value={from}
          onChange={(v) => {
            setFrom(v);
            setPage(1);
          }}
        />
        <DateFilter
          label="To"
          value={to}
          onChange={(v) => {
            setTo(v);
            setPage(1);
          }}
        />
      </FilterBar>

      <div className="rounded-lg border border-border bg-card">
        <QueryPanel result={query} skeleton={<TableSkeleton rows={8} cols={8} />}>
          {items.length > 0 ? (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Symbol</TableHead>
                    <TableHead>Side</TableHead>
                    <TableHead className="text-right">Entry</TableHead>
                    <TableHead className="text-right">Exit</TableHead>
                    <TableHead className="text-right">Qty</TableHead>
                    <TableHead className="text-right">Fees</TableHead>
                    <TableHead className="text-right">Net PnL</TableHead>
                    <TableHead>Close</TableHead>
                    <TableHead className="text-right">Duration</TableHead>
                    <TableHead className="text-right">Closed</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((trade) => (
                    <TableRow
                      key={trade.id}
                      className="cursor-pointer"
                      onClick={() => setSelected(trade)}
                    >
                      <TableCell className="font-medium">{trade.symbol}</TableCell>
                      <TableCell>
                        <SideBadge side={trade.side} />
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatCurrency(trade.entryPrice)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatCurrency(trade.exitPrice)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatNumber(trade.quantity)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-muted-foreground">
                        {formatCurrency(trade.fee)}
                      </TableCell>
                      <TableCell className="text-right">
                        <PnlText value={trade.netPnL} />
                      </TableCell>
                      <TableCell>
                        {trade.closeReason ? (
                          <Badge
                            variant={closeReasonVariant[trade.closeReason] ?? "secondary"}
                          >
                            {trade.closeReason}
                          </Badge>
                        ) : (
                          <span className="text-muted-foreground">—</span>
                        )}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatDuration(trade.openedAt, trade.closedAt)}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatRelativeTime(trade.closedAt)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <div className="border-t border-border px-4 py-3">
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  totalCount={data?.totalCount ?? 0}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                />
              </div>
            </>
          ) : (
            <EmptyState
              title="No trades found"
              description={
                symbol || side || from || to
                  ? "No trades match the current filters."
                  : "Realized trades will appear here once positions are closed."
              }
            />
          )}
        </QueryPanel>
      </div>

      <Dialog open={Boolean(selected)} onOpenChange={(open) => !open && setSelected(null)}>
        <DialogContent className="max-w-md">
          {selected && (
            <>
              <DialogHeader>
                <DialogTitle className="flex items-center gap-2">
                  {selected.symbol}
                  <SideBadge side={selected.side} />
                </DialogTitle>
                <DialogDescription>
                  Trade details
                </DialogDescription>
              </DialogHeader>
              <div className="space-y-3 text-sm">
                <Row label="Net PnL">
                  <PnlText value={selected.netPnL} />
                </Row>
                <Row label="Gross PnL">
                  <PnlText value={selected.grossPnL} />
                </Row>
                <Row label="Fee">
                  <span className="tabular-nums text-muted-foreground">
                    {formatCurrency(selected.fee)}
                  </span>
                </Row>
                <Separator />
                <Row label="Quantity">
                  <span className="tabular-nums">{formatNumber(selected.quantity)}</span>
                </Row>
                <Row label="Entry price">
                  <span className="tabular-nums">{formatCurrency(selected.entryPrice)}</span>
                </Row>
                <Row label="Exit price">
                  <span className="tabular-nums">{formatCurrency(selected.exitPrice)}</span>
                </Row>
                <Separator />
                <Row label="Opened at">
                  <span className="tabular-nums text-muted-foreground">
                    {formatDateTime(selected.openedAt)}
                  </span>
                </Row>
                <Row label="Closed at">
                  <span className="tabular-nums text-muted-foreground">
                    {formatDateTime(selected.closedAt)}
                  </span>
                </Row>
                <Row label="Duration">
                  <span className="tabular-nums text-muted-foreground">
                    {formatDuration(selected.openedAt, selected.closedAt)}
                  </span>
                </Row>
                {selected.closeReason && (
                  <>
                    <Separator />
                    <Row label="Close reason">
                      <Badge
                        variant={closeReasonVariant[selected.closeReason] ?? "secondary"}
                      >
                        {selected.closeReason}
                      </Badge>
                    </Row>
                  </>
                )}
              </div>
            </>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{children}</span>
    </div>
  );
}
