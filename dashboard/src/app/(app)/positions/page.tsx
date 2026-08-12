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
import { FilterBar, SideFilter, TextFilter } from "@/components/shared/filters";
import { fetchOpenPositions, fetchTradingOverview } from "@/services/dashboard-service";
import {
  formatCurrency,
  formatNumber,
  formatPercent,
  formatRelativeTime,
} from "@/lib/formatters";
import { Badge } from "@/components/ui/badge";

export default function PositionsPage() {
  const { token } = useAuth();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [symbol, setSymbol] = useState("");
  const [side, setSide] = useState("");
  const debouncedSymbol = useDebouncedValue(symbol);

  const query = useAuthedQuery(
    ["positions", { page, pageSize, symbol: debouncedSymbol, side }],
    (t) =>
      fetchOpenPositions(t, {
        page,
        pageSize,
        symbol: debouncedSymbol || undefined,
        side: side || undefined,
      }),
    { refetchInterval: 10_000, placeholderData: (prev) => prev }
  );

  const summary = useAuthedQuery(
    ["trading", "summary", { symbol: debouncedSymbol, side }],
    (t) =>
      fetchTradingOverview(t, {
        symbol: debouncedSymbol || undefined,
        side: side || undefined,
      }),
    { refetchInterval: 15_000 }
  );

  const data = query.data;
  const items = data?.items ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Open Positions"
        description="Currently open, partially closed and pending positions."
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Open Positions"
          value={summary.data?.positions.openPositionCount}
          loading={summary.isLoading}
        />
        <StatCard
          label="Long / Short"
          value={
            <span className="flex items-center gap-2">
              <span className="text-profit">
                {summary.data?.positions.longPositionCount ?? 0}
              </span>
              <span className="text-muted-foreground">/</span>
              <span className="text-loss">
                {summary.data?.positions.shortPositionCount ?? 0}
              </span>
            </span>
          }
          loading={summary.isLoading}
        />
        <StatCard
          label="Open Quantity"
          value={formatNumber(summary.data?.positions.totalOpenQuantity)}
          loading={summary.isLoading}
        />
        <StatCard
          label="Total Unrealized PnL"
          value={
            <PnlText value={summary.data?.positions.totalUnrealizedPnL} className="text-2xl" />
          }
          loading={summary.isLoading}
        />
      </div>

      <FilterBar
        onReset={() => {
          setSymbol("");
          setSide("");
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
      </FilterBar>

      <div className="rounded-lg border border-border bg-card">
        <QueryPanel result={query} skeleton={<TableSkeleton rows={8} cols={7} />}>
          {items.length > 0 ? (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Symbol</TableHead>
                    <TableHead>Side</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="text-right">Qty</TableHead>
                    <TableHead className="text-right">Remaining</TableHead>
                    <TableHead className="text-right">Entry</TableHead>
                    <TableHead className="text-right">Current</TableHead>
                    <TableHead className="text-right">Leverage</TableHead>
                    <TableHead className="text-right">Unrealized PnL</TableHead>
                    <TableHead className="text-right">Opened</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((pos) => {
                    const pnlPercent =
                      pos.entryPrice > 0 && pos.side === "Buy"
                        ? ((pos.currentPrice - pos.entryPrice) / pos.entryPrice) * 100
                        : pos.entryPrice > 0 && pos.side === "Sell"
                          ? ((pos.entryPrice - pos.currentPrice) / pos.entryPrice) * 100
                          : 0;
                    return (
                      <TableRow key={pos.id}>
                        <TableCell className="font-medium">{pos.symbol}</TableCell>
                        <TableCell>
                          <SideBadge side={pos.side} />
                        </TableCell>
                        <TableCell>
                          <Badge variant="secondary">{pos.status}</Badge>
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatNumber(pos.quantity)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatNumber(pos.remainingQuantity)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatCurrency(pos.entryPrice)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatCurrency(pos.currentPrice)}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {pos.leverage ? `${pos.leverage}x` : "—"}
                        </TableCell>
                        <TableCell className="text-right">
                          <PnlText value={pos.unrealizedPnL} showPercent={pnlPercent} />
                        </TableCell>
                        <TableCell className="text-right text-xs text-muted-foreground">
                          {formatRelativeTime(pos.openedAt)}
                        </TableCell>
                      </TableRow>
                    );
                  })}
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
              title="No open positions"
              description={
                symbol || side
                  ? "No positions match the current filters."
                  : "Open positions will appear here when the bot opens a trade."
              }
            />
          )}
        </QueryPanel>
      </div>
    </div>
  );
}
