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
import { Pagination } from "@/components/shared/pagination";
import { QueryPanel, TableSkeleton } from "@/components/shared/query-panel";
import { EmptyState } from "@/components/shared/empty-state";
import {
  FilterBar,
  SideFilter,
  StatusFilter,
  TextFilter,
} from "@/components/shared/filters";
import { fetchActiveOrders, fetchTradingOverview } from "@/services/dashboard-service";
import { formatCurrency, formatNumber, formatRelativeTime } from "@/lib/formatters";
import { Badge } from "@/components/ui/badge";

const ORDER_STATUSES = [
  "Created",
  "Submitted",
  "Accepted",
  "PartiallyFilled",
  "Pending",
  "New",
  "ReadyForExchange",
  "Submitting",
  "Unknown",
];

const orderStatusVariant: Record<string, "success" | "warning" | "muted" | "secondary" | "info" | "destructive"> = {
  Filled: "success",
  PartiallyFilled: "info",
  Pending: "warning",
  New: "warning",
  Created: "secondary",
  Submitted: "secondary",
  Accepted: "secondary",
  ReadyForExchange: "secondary",
  Submitting: "secondary",
  Cancelled: "muted",
  Rejected: "destructive",
  Failed: "destructive",
  ValidationFailed: "destructive",
  Expired: "muted",
};

export default function OrdersPage() {
  const { token } = useAuth();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [symbol, setSymbol] = useState("");
  const [side, setSide] = useState("");
  const [status, setStatus] = useState("");
  const debouncedSymbol = useDebouncedValue(symbol);

  const query = useAuthedQuery(
    ["orders", { page, pageSize, symbol: debouncedSymbol, side, status }],
    (t) =>
      fetchActiveOrders(t, {
        page,
        pageSize,
        symbol: debouncedSymbol || undefined,
        side: side || undefined,
        status: status || undefined,
      }),
    { refetchInterval: 10_000, placeholderData: (prev) => prev }
  );

  const summary = useAuthedQuery(
    ["trading", "summary", { symbol: debouncedSymbol }],
    (t) => fetchTradingOverview(t, { symbol: debouncedSymbol || undefined }),
    { refetchInterval: 15_000 }
  );

  const data = query.data;
  const items = data?.items ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Active Orders"
        description="Orders that have not reached a terminal state (filled, cancelled, rejected or failed)."
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Total Orders" value={summary.data?.orders.totalOrders} loading={summary.isLoading} />
        <StatCard label="Open Orders" value={summary.data?.orders.openOrders} loading={summary.isLoading} />
        <StatCard label="Filled" value={summary.data?.orders.filledOrders} loading={summary.isLoading} />
        <StatCard
          label="Cancelled / Rejected"
          value={
            (summary.data?.orders.cancelledOrders ?? 0) +
            (summary.data?.orders.rejectedOrders ?? 0)
          }
          loading={summary.isLoading}
        />
      </div>

      <FilterBar
        onReset={() => {
          setSymbol("");
          setSide("");
          setStatus("");
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
        <StatusFilter
          value={status}
          onChange={(v) => {
            setStatus(v);
            setPage(1);
          }}
          options={ORDER_STATUSES}
          placeholder="Order status"
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
                    <TableHead>Type</TableHead>
                    <TableHead className="text-right">Qty</TableHead>
                    <TableHead className="text-right">Price</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="text-right">Created</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((order) => (
                    <TableRow key={order.id}>
                      <TableCell className="font-medium">{order.symbol}</TableCell>
                      <TableCell>
                        <SideBadge side={order.side} />
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {order.type}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatNumber(order.quantity)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {order.price > 0 ? formatCurrency(order.price) : "Market"}
                      </TableCell>
                      <TableCell>
                        <Badge
                          variant={orderStatusVariant[order.status] ?? "secondary"}
                        >
                          {order.status}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {formatRelativeTime(order.createdAt)}
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
              title="No active orders"
              description={
                symbol || side || status
                  ? "No orders match the current filters."
                  : "Active orders will appear here when the bot places orders."
              }
            />
          )}
        </QueryPanel>
      </div>
    </div>
  );
}
