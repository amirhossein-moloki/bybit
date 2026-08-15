"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  Activity,
  BarChart3,
  Bell,
  CandlestickChart,
  FileBarChart2,
  HeartPulse,
  LayoutDashboard,
  ListOrdered,
  Newspaper,
  RadioTower,
  ScrollText,
  Send,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useIsMobile } from "@/hooks/use-is-mobile";

const navigation = [
  { href: "/overview", label: "Overview", icon: LayoutDashboard },
  { href: "/positions", label: "Positions", icon: CandlestickChart },
  { href: "/orders", label: "Orders", icon: ListOrdered },
  { href: "/trades", label: "Trades", icon: ScrollText },
  { href: "/performance", label: "Performance", icon: BarChart3 },
  { href: "/health", label: "System Health", icon: HeartPulse },
  { href: "/alerts", label: "Alerts", icon: Bell },
  { href: "/events", label: "Events", icon: RadioTower },
  { href: "/reports", label: "Reports", icon: FileBarChart2 },
  { href: "/integrations/telegram", label: "Telegram", icon: Send },
];

function NavItem({
  href,
  label,
  icon: Icon,
  active,
  onClick,
}: {
  href: string;
  label: string;
  icon: typeof LayoutDashboard;
  active: boolean;
  onClick?: () => void;
}) {
  return (
    <Link
      href={href}
      onClick={onClick}
      className={cn(
        "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
        active
          ? "bg-primary/10 text-primary"
          : "text-muted-foreground hover:bg-accent hover:text-foreground"
      )}
    >
      <Icon className="h-4 w-4 shrink-0" />
      {label}
    </Link>
  );
}

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();

  return (
    <aside className="flex h-full w-60 shrink-0 flex-col border-r border-border bg-card">
      <div className="flex h-14 items-center gap-2 border-b border-border px-4">
        <div className="flex h-7 w-7 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <Activity className="h-4 w-4" />
        </div>
        <div className="leading-tight">
          <p className="text-sm font-semibold tracking-tight">Trading Bot</p>
          <p className="text-[10px] uppercase tracking-wider text-muted-foreground">
            Operations
          </p>
        </div>
      </div>
      <nav className="flex-1 space-y-1 overflow-y-auto p-3">
        {navigation.map((item) => {
          const active =
            pathname === item.href || pathname.startsWith(`${item.href}/`);
          return (
            <NavItem
              key={item.href}
              {...item}
              active={active}
              onClick={onNavigate}
            />
          );
        })}
      </nav>
      <div className="border-t border-border p-3">
        <p className="px-3 text-[10px] uppercase tracking-wider text-muted-foreground">
          Telegram Signal Trading Engine
        </p>
      </div>
    </aside>
  );
}
