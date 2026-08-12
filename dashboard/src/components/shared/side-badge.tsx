import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import type { OrderSide } from "@/types/enums";
import { ArrowUpRight, ArrowDownRight } from "lucide-react";

export function SideBadge({
  side,
  className,
}: {
  side: OrderSide | string | null | undefined;
  className?: string;
}) {
  const isBuy = String(side).toLowerCase() === "buy";
  return (
    <Badge
      variant={isBuy ? "success" : "destructive"}
      className={cn("gap-1 font-semibold", className)}
    >
      {isBuy ? (
        <ArrowUpRight className="h-3 w-3" />
      ) : (
        <ArrowDownRight className="h-3 w-3" />
      )}
      {isBuy ? "LONG" : "SHORT"}
    </Badge>
  );
}
