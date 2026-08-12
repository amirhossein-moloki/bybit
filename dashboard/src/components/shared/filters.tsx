"use client";

import { useId } from "react";
import { Filter, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export function SideFilter({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <Select value={value || undefined} onValueChange={(v) => onChange(v === "all" ? "" : v)}>
      <SelectTrigger className="h-9 w-[110px]">
        <SelectValue placeholder="Side" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">All sides</SelectItem>
        <SelectItem value="Buy">Buy / Long</SelectItem>
        <SelectItem value="Sell">Sell / Short</SelectItem>
      </SelectContent>
    </Select>
  );
}

export function StatusFilter({
  value,
  onChange,
  options,
  placeholder = "Status",
}: {
  value: string;
  onChange: (value: string) => void;
  options: string[];
  placeholder?: string;
}) {
  return (
    <Select value={value || undefined} onValueChange={(v) => onChange(v === "all" ? "" : v)}>
      <SelectTrigger className="h-9 w-[150px]">
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">All statuses</SelectItem>
        {options.map((opt) => (
          <SelectItem key={opt} value={opt}>
            {opt}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

export function TextFilter({
  value,
  onChange,
  placeholder,
  className,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
}) {
  return (
    <div className={`relative ${className ?? ""}`}>
      <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
      <Input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="h-9 pl-8"
      />
    </div>
  );
}

export function DateFilter({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const id = useId();
  return (
    <div className="flex items-center gap-1.5">
      <Label htmlFor={id} className="text-xs text-muted-foreground">
        {label}
      </Label>
      <Input
        id={id}
        type="datetime-local"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-9 w-auto text-xs"
      />
    </div>
  );
}

export function FilterBar({
  onReset,
  children,
}: {
  onReset: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-end gap-3 rounded-lg border border-border bg-card p-3">
      <div className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        <Filter className="h-3.5 w-3.5" />
        Filters
      </div>
      {children}
      <Button
        variant="ghost"
        size="sm"
        className="h-9 gap-1 text-xs"
        onClick={onReset}
      >
        <X className="h-3.5 w-3.5" />
        Reset
      </Button>
    </div>
  );
}
