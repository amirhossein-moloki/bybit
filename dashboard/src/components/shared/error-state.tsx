"use client";

import { AlertTriangle, AlertOctagon, ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ApiError, NetworkError, TimeoutError } from "@/lib/api-client";

function CorrelationId({ id }: { id?: string }) {
  if (!id) return null;
  return (
    <p className="mt-2 text-[11px] text-muted-foreground">
      Correlation ID: <code className="font-mono">{id}</code>
    </p>
  );
}

export function ErrorState({
  error,
  onRetry,
}: {
  error: unknown;
  onRetry?: () => void;
}) {
  if (error instanceof ApiError) {
    const Icon =
      error.status === 401 || error.status === 403 ? ShieldAlert : AlertTriangle;
    return (
      <div className="flex flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        <div className="flex h-12 w-12 items-center justify-center rounded-full bg-muted">
          <Icon className="h-6 w-6 text-warning" />
        </div>
        <div>
          <p className="text-sm font-medium">
            {error.status === 401
              ? "Authentication required"
              : error.status === 403
                ? "Permission denied"
                : "Request failed"}
          </p>
          <p className="mt-1 max-w-md text-xs text-muted-foreground">
            {error.message}
          </p>
          <CorrelationId id={error.correlationId} />
        </div>
        {onRetry && (
          <Button variant="outline" size="sm" onClick={onRetry}>
            Retry
          </Button>
        )}
      </div>
    );
  }

  if (error instanceof NetworkError || error instanceof TimeoutError) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 px-6 py-16 text-center">
        <div className="flex h-12 w-12 items-center justify-center rounded-full bg-muted">
          <AlertTriangle className="h-6 w-6 text-warning" />
        </div>
        <div>
          <p className="text-sm font-medium">Connection problem</p>
          <p className="mt-1 max-w-md text-xs text-muted-foreground">
            {error.message}
          </p>
        </div>
        {onRetry && (
          <Button variant="outline" size="sm" onClick={onRetry}>
            Retry
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-16 text-center">
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-muted">
        <AlertOctagon className="h-6 w-6 text-loss" />
      </div>
      <div>
        <p className="text-sm font-medium">Something went wrong</p>
        <p className="mt-1 max-w-md text-xs text-muted-foreground">
          An unexpected error occurred while loading this data.
        </p>
      </div>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
}
