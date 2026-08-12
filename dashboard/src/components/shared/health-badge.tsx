import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { normalizeHealth } from "@/lib/status";
import type { HealthStatus } from "@/types/enums";
import { Circle } from "lucide-react";

const healthVariant: Record<HealthStatus, "success" | "warning" | "critical" | "muted"> = {
  Healthy: "success",
  Degraded: "warning",
  Unhealthy: "critical",
  Unknown: "muted",
};

export function HealthBadge({
  value,
  className,
}: {
  value: string | null | undefined;
  className?: string;
}) {
  const status = normalizeHealth(value);
  return (
    <Badge variant={healthVariant[status]} className={cn(className)}>
      <Circle className="h-2 w-2 fill-current" />
      {status}
    </Badge>
  );
}
