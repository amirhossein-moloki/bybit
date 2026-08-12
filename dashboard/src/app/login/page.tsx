"use client";

import { useState, useEffect, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { Activity, Eye, EyeOff, KeyRound, Loader2 } from "lucide-react";
import { useAuth } from "@/lib/auth";
import { apiGet } from "@/lib/api-client";
import { ApiError, NetworkError } from "@/lib/api-client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { ApiSuccess } from "@/types/api";
import type { DashboardOverviewDto } from "@/types/dashboard";

export default function LoginPage() {
  const router = useRouter();
  const { login, isAuthenticated, hydrated } = useAuth();
  const [token, setToken] = useState("");
  const [show, setShow] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (hydrated && isAuthenticated) {
      router.replace("/overview");
    }
  }, [hydrated, isAuthenticated, router]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = token.trim();
    if (!trimmed) {
      setError("Enter your dashboard access token.");
      return;
    }

    setLoading(true);
    setError(null);

    try {
      // Validate the token against a real backend endpoint before storing it.
      await apiGet<ApiSuccess<DashboardOverviewDto>>("/api/dashboard/overview", {
        token: trimmed,
      });
      login(trimmed);
      router.replace("/overview");
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 401) {
          setError(
            "Token rejected by the backend. Check that it grants the dashboard.read permission."
          );
        } else if (err.status === 403) {
          setError(
            "This token is valid but lacks the dashboard.read permission."
          );
        } else {
          setError(err.message);
        }
      } else if (err instanceof NetworkError) {
        setError(
          "Could not reach the Trading Bot API. Make sure the backend is running and reachable."
        );
      } else {
        setError("An unexpected error occurred.");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background p-4">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex flex-col items-center gap-2 text-center">
          <div className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <Activity className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-lg font-semibold tracking-tight">
              Trading Bot Dashboard
            </h1>
            <p className="text-sm text-muted-foreground">
              Sign in with your dashboard access token
            </p>
          </div>
        </div>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">API Token</CardTitle>
            <CardDescription>
              The backend authenticates requests via{" "}
              <code className="rounded bg-muted px-1 font-mono text-xs">
                Authorization: Bearer &lt;token&gt;
              </code>
              . Your token is stored only in this browser and sent as a Bearer
              header to the API.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="token">Dashboard token</Label>
                <div className="relative">
                  <KeyRound className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                  <Input
                    id="token"
                    type={show ? "text" : "password"}
                    value={token}
                    onChange={(e) => setToken(e.target.value)}
                    placeholder="Paste your Bearer token"
                    autoComplete="off"
                    autoFocus
                    className="pl-9 pr-9 font-mono"
                  />
                  <button
                    type="button"
                    onClick={() => setShow((v) => !v)}
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                    aria-label={show ? "Hide token" : "Show token"}
                  >
                    {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>
              </div>

              {error && (
                <p className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-loss">
                  {error}
                </p>
              )}

              <Button type="submit" className="w-full" disabled={loading}>
                {loading ? (
                  <Loader2 className="animate-spin" />
                ) : (
                  "Sign in"
                )}
              </Button>
            </form>
          </CardContent>
        </Card>

        <p className="mt-4 text-center text-xs text-muted-foreground">
          No exchange credentials, Telegram tokens or private keys are ever
          handled by this dashboard.
        </p>
      </div>
    </div>
  );
}
