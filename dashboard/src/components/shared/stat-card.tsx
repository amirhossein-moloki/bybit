import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { Info } from "lucide-react";

interface StatCardProps {
  label: string;
  value: ReactNode;
  hint?: string;
  accent?: "profit" | "loss" | "neutral" | "primary";
  sublabel?: ReactNode;
  loading?: boolean;
  className?: string;
}

const accentText: Record<NonNullable<StatCardProps["accent"]>, string> = {
  profit: "text-profit",
  loss: "text-loss",
  neutral: "text-foreground",
  primary: "text-primary",
};

export function StatCard({
  label,
  value,
  hint,
  accent = "neutral",
  sublabel,
  loading,
  className,
}: StatCardProps) {
  return (
    <Card className={cn("overflow-hidden", className)}>
      <CardContent className="p-4">
        <div className="flex items-center justify-between">
          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {label}
          </p>
          {hint && (
            <Tooltip>
              <TooltipTrigger asChild>
                <button className="text-muted-foreground/70 hover:text-foreground">
                  <Info className="h-3.5 w-3.5" />
                </button>
              </TooltipTrigger>
              <TooltipContent className="max-w-xs">{hint}</TooltipContent>
            </Tooltip>
          )}
        </div>
        {loading ? (
          <Skeleton className="mt-2 h-7 w-28" />
        ) : (
          <p
            className={cn(
              "mt-1.5 text-2xl font-semibold tabular tabular-nums tracking-tight",
              accentText[accent]
            )}
          >
            {value}
          </p>
        )}
        {sublabel !== undefined && (
          <div className="mt-1 text-xs text-muted-foreground">{sublabel}</div>
        )}
      </CardContent>
    </Card>
  );
}
