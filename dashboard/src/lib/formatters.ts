export function formatCurrency(
  value: number | null | undefined,
  options: { compact?: boolean; sign?: boolean } = {}
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  const { compact = false, sign = false } = options;
  const abs = Math.abs(value);
  const opts: Intl.NumberFormatOptions = {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: abs >= 1000 ? 2 : 4,
  };
  if (compact) {
    opts.notation = "compact";
    opts.maximumFractionDigits = 2;
  }
  let formatted = new Intl.NumberFormat("en-US", opts).format(value);
  if (sign && value > 0) {
    formatted = `+${formatted}`;
  }
  return formatted;
}

export function formatNumber(
  value: number | null | undefined,
  maxFractionDigits = 4
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return new Intl.NumberFormat("en-US", {
    maximumFractionDigits: maxFractionDigits,
  }).format(value);
}

export function formatPercent(
  value: number | null | undefined,
  { sign = false, digits = 2 } = {}
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  let formatted = `${value.toFixed(digits)}%`;
  if (sign && value > 0) formatted = `+${formatted}`;
  return formatted;
}

export function formatDateTime(
  value: string | Date | null | undefined
): string {
  if (!value) return "—";
  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  }).format(date);
}

export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return "—";
  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", {
    year: "numeric",
    month: "short",
    day: "2-digit",
  }).format(date);
}

export function formatRelativeTime(
  value: string | Date | null | undefined
): string {
  if (!value) return "—";
  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return "—";
  const seconds = Math.floor((Date.now() - date.getTime()) / 1000);
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export function formatDuration(
  start: string | Date | null | undefined,
  end: string | Date | null | undefined
): string {
  if (!start || !end) return "—";
  const from = typeof start === "string" ? new Date(start) : start;
  const to = typeof end === "string" ? new Date(end) : end;
  if (
    Number.isNaN(from.getTime()) ||
    Number.isNaN(to.getTime()) ||
    to < from
  ) {
    return "—";
  }
  const ms = to.getTime() - from.getTime();
  return formatMsDuration(ms);
}

export function formatMsDuration(ms: number): string {
  if (ms < 1000) return "<1s";
  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ${minutes % 60}m`;
  const days = Math.floor(hours / 24);
  return `${days}d ${hours % 24}h`;
}

export function formatTimespan(value: string | null | undefined): string {
  if (!value) return "—";
  // Backend serializes TimeSpan as "hh:mm:ss" or "d.hh:mm:ss"
  const match = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})/.exec(value);
  if (!match) return value;
  const [, days, hours, minutes, seconds] = match;
  const parts: string[] = [];
  if (days && Number(days) > 0) parts.push(`${Number(days)}d`);
  if (hours && Number(hours) > 0) parts.push(`${Number(hours)}h`);
  if (minutes && Number(minutes) > 0) parts.push(`${Number(minutes)}m`);
  if (seconds && Number(seconds) > 0 || parts.length === 0)
    parts.push(`${Number(seconds)}s`);
  return parts.join(" ");
}
