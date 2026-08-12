"use client";

import { useState } from "react";
import { useAuthedQuery } from "@/hooks/use-authed-query";
import { PageHeader } from "@/components/shared/page-header";
import { SeverityBadge } from "@/components/shared/severity-badge";
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
import { FilterBar, TextFilter, DateFilter, StatusFilter } from "@/components/shared/filters";
import { fetchEvents } from "@/services/dashboard-service";
import { formatRelativeTime, formatDateTime } from "@/lib/formatters";

const SEVERITIES = ["CRITICAL", "ERROR", "WARNING", "INFORMATION"];

export default function EventsPage() {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [type, setType] = useState("");
  const [severity, setSeverity] = useState("");
  const [source, setSource] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const query = useAuthedQuery(
    ["events", { page, pageSize, type, severity, source, from, to }],
    (t) =>
      fetchEvents(t, {
        page,
        pageSize,
        type: type || undefined,
        severity: severity || undefined,
        source: source || undefined,
        from: from || undefined,
        to: to || undefined,
      }),
    { refetchInterval: 15_000, placeholderData: (prev) => prev }
  );

  const data = query.data;
  const items = data?.items ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Events"
        description="Monitoring events recorded across workers, exchange connections and the signal pipeline."
      />

      <FilterBar
        onReset={() => {
          setType("");
          setSeverity("");
          setSource("");
          setFrom("");
          setTo("");
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
          value={type}
          onChange={(v) => {
            setType(v);
            setPage(1);
          }}
          placeholder="Filter by type…"
        />
        <TextFilter
          value={source}
          onChange={(v) => {
            setSource(v);
            setPage(1);
          }}
          placeholder="Filter by source…"
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
        <QueryPanel result={query} skeleton={<TableSkeleton rows={8} cols={6} />}>
          {items.length > 0 ? (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Severity</TableHead>
                    <TableHead>Type</TableHead>
                    <TableHead>Source</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead className="text-right">Timestamp</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((event) => (
                    <TableRow key={event.id}>
                      <TableCell>
                        <SeverityBadge severity={event.severity} />
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {event.type}
                      </TableCell>
                      <TableCell className="text-xs">{event.source}</TableCell>
                      <TableCell className="max-w-md">
                        <p className="line-clamp-2 text-sm">{event.message}</p>
                        {event.correlationId && (
                          <p className="mt-0.5 text-[10px] font-mono text-muted-foreground">
                            {event.correlationId}
                          </p>
                        )}
                      </TableCell>
                      <TableCell className="text-right text-xs text-muted-foreground">
                        <p>{formatRelativeTime(event.timestamp)}</p>
                        <p>{formatDateTime(event.timestamp)}</p>
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
              title="No events"
              description={
                type || severity || source || from || to
                  ? "No events match the current filters."
                  : "Monitoring events will appear here as they occur."
              }
            />
          )}
        </QueryPanel>
      </div>
    </div>
  );
}
