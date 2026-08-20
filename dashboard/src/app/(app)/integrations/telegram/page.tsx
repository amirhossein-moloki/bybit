"use client";

import { useState, useEffect, useCallback } from "react";
import { QRCodeSVG } from "qrcode.react";
import {
  CheckCircle2,
  XCircle,
  QrCode,
  LogOut,
  RefreshCw,
  Send,
  Loader2,
  ShieldCheck,
  User,
  Search,
  Radio,
  Check,
  Plus,
} from "lucide-react";

import { useAuth } from "@/lib/auth";
import { useToast } from "@/lib/toast";
import { PageHeader } from "@/components/shared/page-header";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import {
  fetchTelegramStatus,
  startTelegramQrAuth,
  fetchTelegramQrStatus,
  logoutTelegram,
  fetchTelegramDialogs,
  toggleMonitoredChannel,
} from "@/services/telegram-service";
import type { TelegramStatusDto, TelegramQrStatusDto, TelegramDialogDto } from "@/types/telegram";

export default function TelegramIntegrationPage() {
  const { token } = useAuth();
  const { toast } = useToast();
  const [status, setStatus] = useState<TelegramStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [qrSession, setQrSession] = useState<{
    sessionId: string;
    qrData: string;
    expiresAt: string;
  } | null>(null);
  const [qrStatus, setQrStatus] = useState<TelegramQrStatusDto | null>(null);
  const [startingQr, setStartingQr] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);

  // Dialogs / Channel Monitoring state
  const [dialogs, setDialogs] = useState<TelegramDialogDto[]>([]);
  const [loadingDialogs, setLoadingDialogs] = useState(false);
  const [togglingMap, setTogglingMap] = useState<Record<string, boolean>>({});
  const [dialogSearch, setDialogSearch] = useState("");

  const loadStatus = useCallback(async () => {
    if (!token) return;
    try {
      setLoading(true);
      const res = await fetchTelegramStatus(token);
      setStatus(res);
    } catch (err) {
      toast({
        title: "Failed to load Telegram status",
        description: err instanceof Error ? err.message : "Unknown error",
        variant: "error",
      });
    } finally {
      setLoading(false);
    }
  }, [token, toast]);

  const loadDialogs = useCallback(async () => {
    if (!token) return;
    try {
      setLoadingDialogs(true);
      const res = await fetchTelegramDialogs(token);
      setDialogs(res || []);
    } catch (err) {
      console.error("Failed to load Telegram dialogs", err);
    } finally {
      setLoadingDialogs(false);
    }
  }, [token]);

  useEffect(() => {
    loadStatus();
  }, [loadStatus]);

  useEffect(() => {
    if (status?.connected || status?.status === "Active" || status?.status === "Connected" || status?.status === "Listening") {
      loadDialogs();
    }
  }, [status, loadDialogs]);

  const handleToggleChannel = async (dialog: TelegramDialogDto) => {
    if (!token) return;
    const identifier = dialog.username ? `@${dialog.username}` : dialog.id.toString();
    const enable = !dialog.isMonitored;

    try {
      setTogglingMap((prev) => ({ ...prev, [identifier]: true }));
      await toggleMonitoredChannel(token, identifier, enable);
      toast({
        title: enable ? "Channel Added" : "Channel Removed",
        description: `${dialog.title} is now ${enable ? "monitored for signals" : "unmonitored"}.`,
        variant: "success",
      });
      await loadDialogs();
    } catch (err) {
      toast({
        title: "Failed to update channel status",
        description: err instanceof Error ? err.message : "Unknown error",
        variant: "error",
      });
    } finally {
      setTogglingMap((prev) => ({ ...prev, [identifier]: false }));
    }
  };

  const filteredDialogs = dialogs.filter((d) => {
    if (!dialogSearch) return true;
    const term = dialogSearch.toLowerCase();
    return (
      d.title.toLowerCase().includes(term) ||
      d.username.toLowerCase().includes(term) ||
      d.id.toString().includes(term)
    );
  });

  // QR Auth Status Polling Loop
  useEffect(() => {
    if (!token || !qrSession || qrSession.sessionId === "") return;

    const interval = setInterval(async () => {
      try {
        const statusRes = await fetchTelegramQrStatus(token, qrSession.sessionId);
        setQrStatus(statusRes);

        if (statusRes.status === "Connected") {
          clearInterval(interval);
          setQrSession(null);
          toast({
            title: "Telegram Connected",
            description: "Telegram account connected successfully!",
            variant: "success",
          });
          loadStatus();
        } else if (statusRes.status === "Expired" || statusRes.status === "Failed") {
          clearInterval(interval);
          if (statusRes.error) {
            toast({
              title: "QR Auth Failed",
              description: statusRes.error,
              variant: "error",
            });
          }
        } else if (statusRes.qrData && statusRes.qrData !== qrSession.qrData) {
          setQrSession((prev) =>
            prev ? { ...prev, qrData: statusRes.qrData!, expiresAt: statusRes.expiresAt || prev.expiresAt } : null
          );
        }
      } catch (err) {
        console.error("Failed to poll QR status", err);
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [token, qrSession, loadStatus, toast]);

  const handleStartQr = async () => {
    if (!token) return;
    try {
      setStartingQr(true);
      setQrStatus(null);
      const res = await startTelegramQrAuth(token);
      setQrSession({
        sessionId: res.sessionId,
        qrData: res.qrData,
        expiresAt: res.expiresAt,
      });
      toast({
        title: "QR Code Generated",
        description: "Scan the QR code with your Telegram mobile app.",
        variant: "info",
      });
    } catch (err) {
      toast({
        title: "Failed to start QR auth",
        description: err instanceof Error ? err.message : "Unknown error",
        variant: "error",
      });
    } finally {
      setStartingQr(false);
    }
  };

  const handleCancelQr = () => {
    setQrSession(null);
    setQrStatus(null);
  };

  const handleLogout = async () => {
    if (!token) return;
    try {
      setLoggingOut(true);
      await logoutTelegram(token);
      setQrSession(null);
      setQrStatus(null);
      toast({
        title: "Logged Out",
        description: "Telegram account disconnected successfully.",
        variant: "success",
      });
      await loadStatus();
    } catch (err) {
      toast({
        title: "Logout Failed",
        description: err instanceof Error ? err.message : "Unknown error",
        variant: "error",
      });
    } finally {
      setLoggingOut(false);
    }
  };

  const isConnected = status?.connected || status?.status === "Active" || status?.status === "Connected" || status?.status === "Listening";

  return (
    <div className="space-y-6">
      <PageHeader
        title="Telegram Integration"
        description="Connect your Telegram account via QR code to listen for trading signal messages."
      />

      <div className="grid gap-6 md:grid-cols-2">
        {/* Connection Status Card */}
        <Card className="flex flex-col justify-between">
          <CardHeader>
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Send className="h-5 w-5 text-primary" />
                <CardTitle>Telegram Connection</CardTitle>
              </div>
              <Badge variant={isConnected ? "default" : "secondary"}>
                {isConnected ? "Active" : "Not Connected"}
              </Badge>
            </div>
            <CardDescription>
              Manage your Telegram session and active signal channel monitoring.
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            {loading ? (
              <div className="flex items-center gap-2 text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin" />
                <span>Checking connection status...</span>
              </div>
            ) : isConnected && status?.account ? (
              <div className="rounded-lg border border-border bg-muted/30 p-4 space-y-3">
                <div className="flex items-center gap-3">
                  <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 text-primary">
                    <User className="h-5 w-5" />
                  </div>
                  <div>
                    <p className="font-semibold text-foreground">
                      {status.account.firstName} {status.account.lastName}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {status.account.username ? `@${status.account.username}` : `ID: ${status.account.id}`}
                    </p>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-2 pt-2 text-xs border-t border-border">
                  <div>
                    <span className="text-muted-foreground">Status:</span>{" "}
                    <span className="font-medium text-emerald-500">{status.status}</span>
                  </div>
                  <div>
                    <span className="text-muted-foreground">Phone:</span>{" "}
                    <span className="font-medium">{status.account.phone || "Hidden"}</span>
                  </div>
                </div>
              </div>
            ) : (
              <div className="rounded-lg border border-border bg-muted/20 p-4 space-y-2">
                <div className="flex items-center gap-2 text-amber-500 font-medium">
                  <XCircle className="h-4 w-4" />
                  <span>Not Connected</span>
                </div>
                <p className="text-xs text-muted-foreground">
                  Connect Telegram using QR code login to enable automated message ingestion and signal detection.
                </p>
              </div>
            )}
          </CardContent>

          <CardFooter className="border-t border-border pt-4">
            {isConnected ? (
              <Button
                variant="destructive"
                className="w-full"
                onClick={handleLogout}
                disabled={loggingOut}
              >
                {loggingOut ? (
                  <>
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    Disconnecting...
                  </>
                ) : (
                  <>
                    <LogOut className="mr-2 h-4 w-4" />
                    Disconnect Telegram
                  </>
                )}
              </Button>
            ) : !qrSession ? (
              <Button className="w-full" onClick={handleStartQr} disabled={startingQr}>
                {startingQr ? (
                  <>
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    Initializing QR Code...
                  </>
                ) : (
                  <>
                    <QrCode className="mr-2 h-4 w-4" />
                    Connect Telegram
                  </>
                )}
              </Button>
            ) : (
              <Button variant="outline" className="w-full" onClick={handleCancelQr}>
                Cancel Login
              </Button>
            )}
          </CardFooter>
        </Card>

        {/* QR Code Scan Card */}
        <Card className="flex flex-col justify-between">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <QrCode className="h-5 w-5 text-primary" />
              <span>QR Authentication</span>
            </CardTitle>
            <CardDescription>
              Scan the QR code from Telegram app (Settings → Devices → Link Desktop Device)
            </CardDescription>
          </CardHeader>

          <CardContent className="flex flex-col items-center justify-center p-6 space-y-4">
            {qrSession && qrSession.qrData ? (
              <div className="flex flex-col items-center space-y-4">
                <div className="p-4 bg-white rounded-xl shadow-md border border-border">
                  <QRCodeSVG value={qrSession.qrData} size={220} level="M" includeMargin />
                </div>

                <div className="text-center space-y-1">
                  <p className="text-sm font-medium flex items-center justify-center gap-2 text-primary">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Waiting for scan...
                  </p>
                  <p className="text-xs text-muted-foreground">
                    Open Telegram on your phone → Settings → Devices → Link Desktop Device
                  </p>
                </div>

                {qrStatus?.status === "Expired" && (
                  <div className="text-center text-xs text-destructive space-y-2">
                    <p>QR Code expired.</p>
                    <Button size="sm" variant="outline" onClick={handleStartQr}>
                      <RefreshCw className="mr-2 h-3 w-3" /> Refresh QR Code
                    </Button>
                  </div>
                )}
              </div>
            ) : isConnected ? (
              <div className="flex flex-col items-center justify-center py-8 text-center space-y-3">
                <div className="flex h-12 w-12 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-500">
                  <ShieldCheck className="h-6 w-6" />
                </div>
                <div>
                  <p className="font-semibold text-foreground">Account Connected & Active</p>
                  <p className="text-xs text-muted-foreground mt-1">
                    Your Telegram session is active and securely persisted. Signals are ingested automatically.
                  </p>
                </div>
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-12 text-center text-muted-foreground space-y-3">
                <QrCode className="h-12 w-12 stroke-[1.5] text-muted-foreground/50" />
                <p className="text-sm">Click &quot;Connect Telegram&quot; to generate a QR login code.</p>
              </div>
            )}
          </CardContent>

          <CardFooter className="border-t border-border pt-4 text-xs text-muted-foreground">
            Session stored securely in persistent encrypted volume. No terminal interaction required.
          </CardFooter>
        </Card>
      </div>

      {/* Monitored Channels & Groups Section */}
      {isConnected && (
        <Card>
          <CardHeader>
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Radio className="h-5 w-5 text-primary" />
                  <span>Monitored Signal Channels & Groups</span>
                </CardTitle>
                <CardDescription>
                  Select which channels or groups from your Telegram account should be monitored for trading signals.
                </CardDescription>
              </div>
              <Button size="sm" variant="outline" onClick={loadDialogs} disabled={loadingDialogs}>
                <RefreshCw className={`mr-2 h-4 w-4 ${loadingDialogs ? "animate-spin" : ""}`} />
                Refresh Channels
              </Button>
            </div>
          </CardHeader>

          <CardContent className="space-y-4">
            <div className="relative">
              <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Search channels & groups by title, @username or ID..."
                className="pl-9"
                value={dialogSearch}
                onChange={(e) => setDialogSearch(e.target.value)}
              />
            </div>

            {loadingDialogs ? (
              <div className="flex items-center justify-center py-12 text-muted-foreground gap-2">
                <Loader2 className="h-5 w-5 animate-spin text-primary" />
                <span>Loading available channels & groups from Telegram...</span>
              </div>
            ) : filteredDialogs.length === 0 ? (
              <div className="text-center py-12 text-muted-foreground border border-dashed rounded-lg">
                <p>No channels or groups found matching your search.</p>
              </div>
            ) : (
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {filteredDialogs.map((dialog) => {
                  const identifier = dialog.username ? `@${dialog.username}` : dialog.id.toString();
                  const isToggling = togglingMap[identifier];

                  return (
                    <div
                      key={dialog.id}
                      className={`flex flex-col justify-between p-4 rounded-lg border transition-colors ${
                        dialog.isMonitored
                          ? "border-primary/50 bg-primary/5"
                          : "border-border bg-card hover:bg-muted/30"
                      }`}
                    >
                      <div className="space-y-2">
                        <div className="flex items-start justify-between gap-2">
                          <p className="font-semibold text-sm line-clamp-1">{dialog.title}</p>
                          <Badge variant={dialog.isMonitored ? "default" : "outline"} className="text-[10px] shrink-0">
                            {dialog.isMonitored ? "Monitored" : dialog.isChannel ? "Channel" : "Group"}
                          </Badge>
                        </div>
                        <p className="text-xs text-muted-foreground font-mono">
                          {dialog.username ? `@${dialog.username}` : `ID: ${dialog.id}`}
                        </p>
                      </div>

                      <div className="pt-3 mt-2 border-t border-border/50 flex items-center justify-between">
                        <span className="text-[11px] text-muted-foreground">
                          {dialog.isChannel ? "Channel" : "Group"}
                        </span>
                        <Button
                          size="sm"
                          variant={dialog.isMonitored ? "destructive" : "default"}
                          className="h-8 text-xs"
                          onClick={() => handleToggleChannel(dialog)}
                          disabled={isToggling}
                        >
                          {isToggling ? (
                            <Loader2 className="h-3.5 w-3.5 animate-spin" />
                          ) : dialog.isMonitored ? (
                            <>
                              <XCircle className="mr-1.5 h-3.5 w-3.5" /> Stop Monitoring
                            </>
                          ) : (
                            <>
                              <Plus className="mr-1.5 h-3.5 w-3.5" /> Monitor Signal
                            </>
                          )}
                        </Button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
