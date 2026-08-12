"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";

const STORAGE_KEY = "tradingbot.dashboard.token";

interface AuthContextValue {
  token: string | null;
  isAuthenticated: boolean;
  hydrated: boolean;
  login: (token: string) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function readStoredToken(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [hydrated, setHydrated] = useState(false);
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    setToken(readStoredToken());
    setHydrated(true);
  }, []);

  const login = useCallback(
    (value: string) => {
      const trimmed = value.trim();
      setToken(trimmed);
      try {
        if (trimmed) {
          window.localStorage.setItem(STORAGE_KEY, trimmed);
        } else {
          window.localStorage.removeItem(STORAGE_KEY);
        }
      } catch {
        // Storage unavailable; keep the token in memory only.
      }
    },
    []
  );

  const logout = useCallback(() => {
    setToken(null);
    try {
      window.localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Ignore storage failures.
    }
    router.replace("/login");
  }, [router]);

  useEffect(() => {
    const onUnauthorized = () => logout();
    window.addEventListener("tradingbot:unauthorized", onUnauthorized);
    return () =>
      window.removeEventListener("tradingbot:unauthorized", onUnauthorized);
  }, [logout]);

  const value = useMemo<AuthContextValue>(
    () => ({
      token,
      isAuthenticated: Boolean(token),
      hydrated,
      login,
      logout,
    }),
    [token, hydrated, login, logout]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return ctx;
}

export function notifyUnauthorized() {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new Event("tradingbot:unauthorized"));
  }
}
