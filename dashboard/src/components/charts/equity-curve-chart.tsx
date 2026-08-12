"use client";

import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { chartColors } from "./chart-colors";
import { formatCurrency, formatDateTime } from "@/lib/formatters";

interface EquityCurveChartProps {
  data: Array<{
    tradeIndex: number;
    closedAt: string;
    equity: number;
    cumulativePnL: number;
    drawdownPercentage: number;
  }>;
  height?: number;
}

export function EquityCurveChart({ data, height = 280 }: EquityCurveChartProps) {
  if (!data || data.length === 0) {
    return (
      <div
        style={{ height }}
        className="flex items-center justify-center text-sm text-muted-foreground"
      >
        No equity data available
      </div>
    );
  }

  return (
    <div style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          <defs>
            <linearGradient id="equityFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={chartColors.primary} stopOpacity={0.35} />
              <stop offset="100%" stopColor={chartColors.primary} stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid stroke={chartColors.grid} vertical={false} />
          <XAxis
            dataKey="tradeIndex"
            stroke={chartColors.axis}
            fontSize={11}
            tickLine={false}
            axisLine={false}
            tickFormatter={(v) => `#${v}`}
            minTickGap={24}
          />
          <YAxis
            stroke={chartColors.axis}
            fontSize={11}
            tickLine={false}
            axisLine={false}
            width={60}
            tickFormatter={(v) => formatCurrency(Number(v), { compact: true })}
          />
          <Tooltip
            contentStyle={{
              background: "hsl(225 30% 9%)",
              border: "1px solid hsl(224 20% 16%)",
              borderRadius: 8,
              fontSize: 12,
            }}
            labelFormatter={(_, payload) => {
              const point = payload?.[0]?.payload as EquityCurveChartProps["data"][number];
              return point ? formatDateTime(point.closedAt) : "";
            }}
            formatter={(value: number | string, name: string) => [
              formatCurrency(Number(value)),
              name === "equity" ? "Equity" : name === "cumulativePnL" ? "Cum. PnL" : name,
            ]}
          />
          <Area
            type="monotone"
            dataKey="equity"
            stroke={chartColors.primary}
            strokeWidth={2}
            fill="url(#equityFill)"
            dot={false}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
