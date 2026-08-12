"use client";

import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { chartColors } from "./chart-colors";
import { formatCurrency } from "@/lib/formatters";

interface PnlBarChartProps {
  data: Array<{ label: string; netPnL: number }>;
  height?: number;
}

export function PnlBarChart({ data, height = 280 }: PnlBarChartProps) {
  if (!data || data.length === 0) {
    return (
      <div
        style={{ height }}
        className="flex items-center justify-center text-sm text-muted-foreground"
      >
        No aggregation data available
      </div>
    );
  }

  return (
    <div style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid stroke={chartColors.grid} vertical={false} />
          <XAxis
            dataKey="label"
            stroke={chartColors.axis}
            fontSize={11}
            tickLine={false}
            axisLine={false}
            interval="preserveStartEnd"
            minTickGap={16}
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
            cursor={{ fill: "rgba(148,163,184,0.06)" }}
            contentStyle={{
              background: "hsl(225 30% 9%)",
              border: "1px solid hsl(224 20% 16%)",
              borderRadius: 8,
              fontSize: 12,
            }}
            formatter={(value: number | string) => [formatCurrency(Number(value)), "Net PnL"]}
          />
          <ReferenceLine y={0} stroke={chartColors.muted} strokeDasharray="3 3" />
          <Bar dataKey="netPnL" radius={[3, 3, 0, 0]} maxBarSize={40}>
            {data.map((entry, index) => (
              <Cell
                key={index}
                fill={entry.netPnL >= 0 ? chartColors.profit : chartColors.loss}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
