"use client";

import type { ReactNode } from "react";
import type { UseQueryResult } from "@tanstack/react-query";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/shared/error-state";
import { cn } from "@/lib/utils";

export function TableSkeleton({ rows = 6, cols = 5 }: { rows?: number; cols?: number }) {
  return (
    <div className="space-y-3 p-4">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex gap-4">
          {Array.from({ length: cols }).map((_, j) => (
            <Skeleton
              key={j}
              className={cn("h-4", j === 0 ? "w-24" : "flex-1")}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

export function CardSkeleton({ className }: { className?: string }) {
  return (
    <Card className={className}>
      <CardContent className="p-5">
        <Skeleton className="h-4 w-28" />
        <Skeleton className="mt-3 h-8 w-40" />
      </CardContent>
    </Card>
  );
}

interface QueryPanelProps {
  result: Pick<UseQueryResult, "isLoading" | "isError" | "error" | "refetch">;
  skeleton?: ReactNode;
  errorClassName?: string;
  children: ReactNode;
}

export function QueryPanel({ result, skeleton, errorClassName, children }: QueryPanelProps) {
  if (result.isLoading) {
    return skeleton ?? <TableSkeleton />;
  }

  if (result.isError) {
    return (
      <div className={errorClassName}>
        <ErrorState error={result.error} onRetry={() => result.refetch()} />
      </div>
    );
  }

  return <>{children}</>;
}

interface PanelCardProps {
  title: string;
  subtitle?: string;
  className?: string;
  contentClassName?: string;
  children: ReactNode;
}

export function PanelCard({
  title,
  subtitle,
  className,
  contentClassName,
  children,
}: PanelCardProps) {
  return (
    <Card className={className}>
      <CardHeader className="pb-3">
        <CardTitle>{title}</CardTitle>
        {subtitle && <p className="text-xs text-muted-foreground">{subtitle}</p>}
      </CardHeader>
      <CardContent className={cn("p-0", contentClassName)}>{children}</CardContent>
    </Card>
  );
}
