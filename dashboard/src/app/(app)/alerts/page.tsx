"use client";

import { useState } from "react";
import { useAuth } from "@/lib/auth";
import { useAuthedQuery } from "@/hooks/use-authed-query";
import { PageHeader } from "@/components/shared/page-header";
import { SeverityBadge } from "@/components/shared/severity-badge";
import { HealthBadge } from "@/components/shared/health-badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Pagination } from "@/components/shared/pagination";
import { QueryPanel, TableSkeleton } from "@/components/shared/query-panel";
import { EmptyState } from "@/components/shared/empty-state";
import { FilterBar, TextFilter, StatusFilter } from "@/components/shared/filters";
import { fetchAlerts } from "@/services/dashboard-service";
import { formatRelativeTime, formatDateTime } from "@/lib/formatters";

const SEVERITIES = ["CRITICAL", "ERROR", "WARNING", "INFORMATION"];

export default function AlertsPage() {
  const { token } = useAuth();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [severity, setSeverity] = useState("");
  const [source, setSource] = useState("");
  const [type, setType] = useState("");

  const query = useAuthedQuery(
    ["alerts", { page, pageSize, severity, source, type }],
    (t) =>
      fetchAlerts(t, {
        page,
        pageSize,
        severity: severity || undefined,
        source: source || undefined,
        type: type || undefined,
      }),
    { refetchInterval: 15_000, placeholderData: (prev) => prev }
  );

  const data = query.data;
  const items = data?.items ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Alerts"
        description="Alerts raised by the monitoring and risk engine, in every severity."
      />

      <FilterBar
        onReset={() => {
          setSeverity("");
          setSource("");
          setType("");
          setPage(1);
        }}
      >
        <StatusFilter
          value={severity}
          onChange={(v) => {
            setSeverity(v);
            setPage(1);
          }}
          options={SEVERITIES}
          placeholder="Severity"
        />
        <TextFilter
          value={source}
          onChange={(v) => {
            setSource(v);
            setPage(1);
          }}
          placeholder="Filter by source…"
        />
        <TextFilter
          value={type}
          onChange={(v) => {
            setType(v);
            setPage(1);
          }}
          placeholder="Filter by type…"
        />
      </FilterBar>

      <div className="rounded-lg border border-border bg-card">
        <QueryPanel result={query} skeleton={<TableSkeleton rows={8} cols={6} />}>
          {items.length > 0 ? (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Severity</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Type</TableHead>
                    <TableHead>Source</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead className="text-right">Triggered</TableHead>
                    <TableHead className="text-right">Updated</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((alert) => (
                    <TableRow key={alert.id}>
                      <TableCell>
                        <SeverityBadge severity={alert.severity} />
                      </TableCell>
                      <TableCell>
                        <HealthBadge value={alert.status} />
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {alert.type}
                      </TableCell>
                      <TableCell className="text-xs">{alert.source}</TableCell>
                      <TableCell className="max-w-md">
                        <p className="line-clamp-2 text-sm">{alert.message}</p>
                        {alert.correlationId && (
                          <p className="mt-0.5 text-[10px] font-mono text-muted-foreground">
                            {alert.correlationId}
                          </p>
                        )}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        <p>{formatRelativeTime(alert.triggeredAt)}</p>
                        <p>{formatDateTime(alert.triggeredAt)}</p>
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        {alert.lastUpdatedAt ? formatRelativeTime(alert.lastUpdatedAt) : "—"}
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
              title="No alerts"
              description={
                severity || source || type
                  ? "No alerts match the current filters."
                  : "Alerts raised by the monitoring engine will appear here."
              }
            />
          )}
        </QueryPanel>
      </div>
    </div>
  );
}
