import type { HealthStatus, Severity } from "@/types/enums";

export function normalizeHealth(value: string | null | undefined): HealthStatus {
  const v = (value ?? "Unknown").toLowerCase();
  if (v === "healthy" || v === "ok" || v === "operational" || v === "connected") {
    return "Healthy";
  }
  if (v === "degraded" || v === "degrading" || v === "stale") {
    return "Degraded";
  }
  if (
    v === "unhealthy" ||
    v === "critical" ||
    v === "failed" ||
    v === "offline" ||
    v === "disconnected" ||
    v === "fatal"
  ) {
    return "Unhealthy";
  }
  return "Unknown";
}

export function normalizeSeverity(value: string | null | undefined): Severity {
  const v = (value ?? "").toUpperCase();
  if (v.includes("CRIT")) return "CRITICAL";
  if (v.includes("ERROR")) return "ERROR";
  if (v.includes("WARN")) return "WARNING";
  if (v.includes("INFO")) return "INFORMATION";
  return "WARNING";
}

export function isPositiveHealth(status: HealthStatus): boolean {
  return status === "Healthy";
}

export const HEALTH_LABELS: Record<HealthStatus, string> = {
  Healthy: "Healthy",
  Degraded: "Degraded",
  Unhealthy: "Unhealthy",
  Unknown: "Unknown",
};

export const SEVERITY_RANK: Record<Severity, number> = {
  CRITICAL: 4,
  ERROR: 3,
  WARNING: 2,
  INFORMATION: 1,
  INFO: 1,
};

export function severityRank(severity: string): number {
  return SEVERITY_RANK[normalizeSeverity(severity)] ?? 0;
}

export function isActiveOrderStatus(status: string): boolean {
  return !["Filled", "Cancelled", "Rejected", "Failed", "Expired", "ValidationFailed"].includes(status);
}
