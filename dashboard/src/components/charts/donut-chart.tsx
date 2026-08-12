"use client";

import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { chartColors } from "./chart-colors";

interface DonutChartProps {
  data: Array<{ name: string; value: number; color?: string }>;
  height?: number;
  centerLabel?: string;
  centerValue?: string;
}

export function DonutChart({
  data,
  height = 200,
  centerLabel,
  centerValue,
}: DonutChartProps) {
  const filtered = data.filter((d) => d.value > 0);

  if (filtered.length === 0) {
    return (
      <div
        style={{ height }}
        className="flex items-center justify-center text-sm text-muted-foreground"
      >
        No data available
      </div>
    );
  }

  return (
    <div style={{ height }} className="relative">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie
            data={filtered}
            dataKey="value"
            nameKey="name"
            cx="50%"
            cy="50%"
            innerRadius="62%"
            outerRadius="88%"
            paddingAngle={2}
            stroke="none"
          >
            {filtered.map((entry, index) => (
              <Cell key={index} fill={entry.color ?? chartColors.primary} />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{
              background: "hsl(225 30% 9%)",
              border: "1px solid hsl(224 20% 16%)",
              borderRadius: 8,
              fontSize: 12,
            }}
          />
        </PieChart>
      </ResponsiveContainer>
      {(centerLabel || centerValue) && (
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <span className="text-lg font-semibold tabular-nums">
            {centerValue}
          </span>
          <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
            {centerLabel}
          </span>
        </div>
      )}
    </div>
  );
}
