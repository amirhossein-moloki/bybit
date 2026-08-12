import { cn } from "@/lib/utils";
import { formatCurrency, formatPercent } from "@/lib/formatters";

export function PnlText({
  value,
  className,
  sign = true,
  showPercent,
}: {
  value: number | null | undefined;
  className?: string;
  sign?: boolean;
  showPercent?: number | null;
}) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return <span className={cn("text-muted-foreground", className)}>—</span>;
  }
  const isPositive = value > 0;
  const isNegative = value < 0;

  return (
    <span
      className={cn(
        "tabular-nums font-medium",
        isPositive && "text-profit",
        isNegative && "text-loss",
        value === 0 && "text-muted-foreground",
        className
      )}
    >
      {formatCurrency(value, { sign })}
      {showPercent !== null && showPercent !== undefined && !Number.isNaN(showPercent) && (
        <span className="ml-1 text-xs opacity-80">
          ({formatPercent(showPercent, { sign })})
        </span>
      )}
    </span>
  );
}
