"use client";

import { useCallback, useEffect, useState } from "react";
import { usePathname } from "next/navigation";
import { LogOut, Menu, RefreshCcw, Radio } from "lucide-react";
import { useAuth } from "@/lib/auth";
import { HealthBadge } from "@/components/shared/health-badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { apiGet } from "@/lib/api-client";
import type { HealthStatusProviderDto } from "@/types/health";
import { formatRelativeTime } from "@/lib/formatters";

const pageTitles: Record<string, string> = {
  "/overview": "Overview",
  "/positions": "Positions",
  "/orders": "Orders",
  "/trades": "Trades",
  "/performance": "Performance",
  "/health": "System Health",
  "/alerts": "Alerts",
  "/events": "Events",
  "/reports": "Reports",
};

export function Topbar({ onMenuClick }: { onMenuClick: () => void }) {
  const pathname = usePathname();
  const { token, logout } = useAuth();
  const title = pageTitles[pathname] ?? "Dashboard";

  const [overallStatus, setOverallStatus] = useState<string>("Unknown");
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);

  const refreshHealth = useCallback(async () => {
    try {
      const data = await apiGet<HealthStatusProviderDto>("/health/status");
      setOverallStatus(data.status);
      setUpdatedAt(new Date());
    } catch {
      setOverallStatus("Unknown");
      setUpdatedAt(null);
    }
  }, []);

  useEffect(() => {
    refreshHealth();
    const interval = setInterval(refreshHealth, 15000);
    return () => clearInterval(interval);
  }, [refreshHealth]);

  const maskedToken = token ? `${token.slice(0, 6)}…${token.slice(-4)}` : "—";

  return (
    <header className="flex h-14 shrink-0 items-center justify-between gap-3 border-b border-border bg-card px-4">
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          className="lg:hidden"
          onClick={onMenuClick}
          aria-label="Open menu"
        >
          <Menu className="h-5 w-5" />
        </Button>
        <h2 className="text-sm font-semibold">{title}</h2>
      </div>
      <div className="flex items-center gap-3">
        <div className="hidden items-center gap-2 rounded-md border border-border px-2.5 py-1.5 sm:flex">
          <Radio className="h-3.5 w-3.5 text-muted-foreground" />
          <span className="text-xs text-muted-foreground">Status</span>
          <HealthBadge value={overallStatus} />
        </div>
        {updatedAt && (
          <span className="hidden text-xs text-muted-foreground md:block">
            Updated {formatRelativeTime(updatedAt)}
          </span>
        )}
        <Button
          variant="ghost"
          size="icon"
          onClick={refreshHealth}
          aria-label="Refresh status"
          className="h-8 w-8"
        >
          <RefreshCcw className="h-4 w-4" />
        </Button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="sm" className="gap-2">
              <span className="flex h-5 w-5 items-center justify-center rounded-full bg-primary/20 text-[10px] font-bold text-primary">
                D
              </span>
              <span className="hidden font-mono text-xs sm:inline">
                {maskedToken}
              </span>
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-56">
            <DropdownMenuLabel className="text-xs">
              Dashboard API access
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              className="text-xs"
              disabled
              onSelect={(e) => e.preventDefault()}
            >
              Bearer token: <code className="ml-1 font-mono">{maskedToken}</code>
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem onClick={logout} className="text-loss">
              <LogOut />
              Log out
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
}
