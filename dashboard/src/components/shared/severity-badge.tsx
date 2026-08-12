import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { normalizeSeverity } from "@/lib/status";
import type { Severity } from "@/types/enums";
import { AlertTriangle, AlertOctagon, Info, CircleAlert } from "lucide-react";

const severityVariant: Record<Severity, "critical" | "destructive" | "warning" | "info" | "muted"> = {
  CRITICAL: "critical",
  ERROR: "destructive",
  WARNING: "warning",
  INFORMATION: "info",
  INFO: "info",
};

const SeverityIcon = {
  CRITICAL: AlertOctagon,
  ERROR: AlertTriangle,
  WARNING: CircleAlert,
  INFORMATION: Info,
  INFO: Info,
};

export function SeverityBadge({
  severity,
  className,
}: {
  severity: string | null | undefined;
  className?: string;
}) {
  const norm = normalizeSeverity(severity);
  const Icon = SeverityIcon[norm];
  return (
    <Badge variant={severityVariant[norm]} className={cn(className)}>
      <Icon className="h-3 w-3" />
      {norm}
    </Badge>
  );
}
