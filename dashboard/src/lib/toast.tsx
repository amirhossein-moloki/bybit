"use client";

import {
  createContext,
  useCallback,
  useContext,
  useState,
  type ReactNode,
} from "react";
import { CheckCircle2, AlertCircle, X } from "lucide-react";
import { cn } from "@/lib/utils";

type ToastVariant = "success" | "error" | "info" | "warning";

interface Toast {
  id: number;
  title: string;
  description?: string;
  variant: ToastVariant;
}

interface ToastContextValue {
  toast: (toast: Omit<Toast, "id">) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

let nextId = 1;

const variantStyles: Record<ToastVariant, string> = {
  success: "border-profit/40 text-profit",
  error: "border-destructive/50 text-loss",
  warning: "border-warning/40 text-warning",
  info: "border-info/40 text-info",
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const dismiss = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback(
    (newToast: Omit<Toast, "id">) => {
      const id = nextId++;
      setToasts((prev) => [...prev, { ...newToast, id }]);
      window.setTimeout(() => dismiss(id), 6000);
    },
    [dismiss]
  );

  return (
    <ToastContext.Provider value={{ toast }}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-full max-w-sm flex-col gap-2">
        {toasts.map((t) => (
          <div
            key={t.id}
            role="status"
            className={cn(
              "pointer-events-auto flex items-start gap-3 rounded-lg border bg-card p-4 shadow-lg animate-slide-in",
              variantStyles[t.variant]
            )}
          >
            {t.variant === "success" && (
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            )}
            {(t.variant === "error" || t.variant === "warning") && (
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
            )}
            {t.variant === "info" && (
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 opacity-70" />
            )}
            <div className="flex-1 text-sm">
              <p className="font-medium text-foreground">{t.title}</p>
              {t.description && (
                <p className="mt-0.5 break-words text-xs text-muted-foreground">
                  {t.description}
                </p>
              )}
            </div>
            <button
              onClick={() => dismiss(t.id)}
              className="rounded p-0.5 text-muted-foreground hover:text-foreground"
              aria-label="Dismiss"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error("useToast must be used within a ToastProvider");
  }
  return ctx;
}
