"use client";

import { useState, useEffect, useCallback, useMemo } from "react";
import { QRCodeSVG } from "qrcode.react";
import {
  Send,
  RefreshCw,
  Search,
  CheckCircle2,
  XCircle,
  Clock,
  AlertTriangle,
  Play,
  Pause,
  Power,
  Shield,
  Activity,
  Plus,
  Trash2,
  FileText,
  Radio,
  Sliders,
  Check,
  ChevronRight,
  Loader2,
  Info,
  User,
  LogOut,
  QrCode,
  ShieldCheck,
  Settings,
} from "lucide-react";

import { useAuth } from "@/lib/auth";
import { useToast } from "@/lib/toast";
import { PageHeader } from "@/components/shared/page-header";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from "@/components/ui/table";
import { Pagination } from "@/components/shared/pagination";

import {
  fetchTelegramStatus,
  startTelegramQrAuth,
  fetchTelegramQrStatus,
  startTelegramOtpAuth,
  verifyTelegramOtpCode,
  submitTelegramPassword,
  logoutTelegram,
  fetchTelegramSources,
  updateTelegramSource,
  deleteTelegramSource,
  syncTelegramSources,
  bulkUpdateSources,
  fetchSourceMessages,
  fetchSourceSignals,
  fetchSourceHealth,
  testTelegramSource,
} from "@/services/telegram-service";

import type {
  TelegramStatusDto,
  TelegramQrStatusDto,
  TelegramSourceDto,
  SyncSourcesResultDto,
  TestSourceResultDto,
  TelegramMessagePreviewDto,
  TelegramSignalPreviewDto,
  TelegramSourceHealthDto,
  TelegramDialogDto,
} from "@/types/telegram";

export default function TelegramControlCenterPage() {
  const { token } = useAuth();
  const { toast } = useToast();

  // Auth & Client Status
  const [status, setStatus] = useState<TelegramStatusDto | null>(null);
  const [loadingStatus, setLoadingStatus] = useState(true);
  const [showAuthDialog, setShowAuthDialog] = useState(false);
  const [authMethod, setAuthMethod] = useState<"qr" | "otp">("qr");

  // QR Login State
  const [qrSession, setQrSession] = useState<{
    sessionId: string;
    qrData: string;
    expiresAt: string;
  } | null>(null);
  const [qrStatus, setQrStatus] = useState<TelegramQrStatusDto | null>(null);
  const [startingQr, setStartingQr] = useState(false);

  // OTP Login State
  const [otpStep, setOtpStep] = useState<1 | 2 | 3>(1); // 1: Phone, 2: Code, 3: Password
  const [phoneNumber, setPhoneNumber] = useState("");
  const [phoneCodeHash, setPhoneCodeHash] = useState("");
  const [verificationCode, setVerificationCode] = useState("");
  const [twoFactorPassword, setTwoFactorPassword] = useState("");
  const [otpLoading, setOtpLoading] = useState(false);
  const [otpError, setOtpError] = useState<string | null>(null);

  const [loggingOut, setLoggingOut] = useState(false);

  // Control Center Sources State
  const [sources, setSources] = useState<TelegramSourceDto[]>([]);
  const [loadingSources, setLoadingSources] = useState(true);
  const [search, setSearch] = useState("");
  const [selectedType, setSelectedType] = useState("All");
  const [selectedStatus, setSelectedStatus] = useState("All");

  // Selection & Bulk Actions State
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [bulkActionLoading, setBulkActionLoading] = useState(false);

  // Sync UX State
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState<SyncSourcesResultDto | null>(null);
  const [showSyncModal, setShowSyncModal] = useState(false);

  // Add Source Wizard State
  const [showAddWizard, setShowAddWizard] = useState(false);
  const [addStep, setAddStep] = useState(1);
  const [dialogs, setDialogs] = useState<TelegramDialogDto[]>([]);
  const [selectedDialog, setSelectedDialog] = useState<TelegramDialogDto | null>(null);
  const [addForm, setAddStepForm] = useState({
    isEnabled: true,
    listenForSignals: true,
    processMessages: true,
  });
  const [creatingSource, setCreatingSource] = useState(false);

  // Source Details Drawer / Modal State
  const [activeSource, setActiveSource] = useState<TelegramSourceDto | null>(null);
  const [detailsTab, setDetailsTab] = useState("overview");
  const [messages, setMessages] = useState<TelegramMessagePreviewDto[]>([]);
  const [signals, setSignals] = useState<TelegramSignalPreviewDto[]>([]);
  const [health, setHealth] = useState<TelegramSourceHealthDto | null>(null);
  const [testResult, setTestResult] = useState<TestSourceResultDto | null>(null);
  const [loadingDetails, setLoadingDetails] = useState(false);
  const [testingSource, setTestingSource] = useState(false);
  const [msgPage, setMsgPage] = useState(1);
  const [sigPage, setSigPage] = useState(1);

  // Pause Duration Modal State
  const [pauseTargetId, setPauseTargetId] = useState<string | null>(null);
  const [pauseMinutes, setPauseMinutes] = useState(60);

  // Load Status & Sources
  const loadStatus = useCallback(async () => {
    if (!token) return;
    try {
      setLoadingStatus(true);
      const res = await fetchTelegramStatus(token);
      setStatus(res);
    } catch (err) {
      console.error("Failed to fetch Telegram status", err);
    } finally {
      setLoadingStatus(false);
    }
  }, [token]);

  const loadSources = useCallback(async () => {
    if (!token) return;
    try {
      setLoadingSources(true);
      const res = await fetchTelegramSources(token, {
        search: search || undefined,
        type: selectedType !== "All" ? selectedType : undefined,
        status: selectedStatus !== "All" ? selectedStatus : undefined,
      });
      setSources(res || []);
    } catch (err) {
      toast({
        title: "Failed to load Telegram sources",
        description: err instanceof Error ? err.message : "Unknown error",
        variant: "error",
      });
    } finally {
      setLoadingSources(false);
    }
  }, [token, search, selectedType, selectedStatus, toast]);

  useEffect(() => {
    loadStatus();
    loadSources();
  }, [loadStatus, loadSources]);

  // QR Auth Polling
  useEffect(() => {
    if (!token || !qrSession || !qrSession.sessionId) return;

    const interval = setInterval(async () => {
      try {
        const res = await fetchTelegramQrStatus(token, qrSession.sessionId);
        setQrStatus(res);

        if (res.status === "Connected") {
          clearInterval(interval);
          setQrSession(null);
          toast({
            title: "Telegram Connected",
            description: "Telegram account connected successfully!",
            variant: "success",
          });
          loadStatus();
          loadSources();
        } else if (res.status === "Expired" || res.status === "Failed") {
          clearInterval(interval);
        } else if (res.qrData && res.qrData !== qrSession.qrData) {
          setQrSession((prev) =>
            prev ? { ...prev, qrData: res.qrData!, expiresAt: res.expiresAt || prev.expiresAt } : null
          );
        }
      } catch (err) {
        console.error("QR Status polling error", err);
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [token, qrSession, loadStatus, loadSources, toast]);

  const handleStartQr = async () => {
    if (!token) return;
    try {
      setStartingQr(true);
      const res = await startTelegramQrAuth(token);
      setQrSession(res);
      toast({
        title: "QR Code Generated",
        description: "Scan the QR code with your Telegram mobile app.",
        variant: "info",
      });
    } catch (err) {
      toast({
        title: "Failed to start QR authentication",
        description: err instanceof Error ? err.message : "Unknown error",
        variant: "error",
      });
    } finally {
      setStartingQr(false);
    }
  };

  // OTP Handlers
  const handleStartOtp = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!token || !phoneNumber.trim()) return;

    setOtpLoading(true);
    setOtpError(null);

    try {
      const res = await startTelegramOtpAuth(token, { phoneNumber: phoneNumber.trim() });
      if (res.success && res.phoneCodeHash) {
        setPhoneCodeHash(res.phoneCodeHash);
        setOtpStep(2);
        toast({
          title: "Verification Code Sent",
          description: "Check your Telegram app or SMS for the code.",
          variant: "info",
        });
      } else {
        setOtpError(res.message || "Failed to send verification code.");
      }
    } catch (err) {
      setOtpError(err instanceof Error ? err.message : "Failed to send code.");
    } finally {
      setOtpLoading(false);
    }
  };

  const handleVerifyOtp = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!token || !verificationCode.trim()) return;

    setOtpLoading(true);
    setOtpError(null);

    try {
      const res = await verifyTelegramOtpCode(token, {
        phoneNumber: phoneNumber.trim(),
        phoneCodeHash,
        code: verificationCode.trim(),
      });

      if (res.requiresPassword) {
        setOtpStep(3);
        toast({
          title: "Two-Factor Auth Required",
          description: "Please enter your Telegram account password.",
          variant: "warning",
        });
      } else if (res.success) {
        setShowAuthDialog(false);
        resetOtpState();
        toast({
          title: "Telegram Connected",
          description: "Authenticated successfully via OTP!",
          variant: "success",
        });
        loadStatus();
        loadSources();
      } else {
        setOtpError(res.message || "Invalid verification code.");
      }
    } catch (err) {
      setOtpError(err instanceof Error ? err.message : "Verification failed.");
    } finally {
      setOtpLoading(false);
    }
  };

  const handleSubmitPassword = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!token || !twoFactorPassword) return;

    setOtpLoading(true);
    setOtpError(null);

    try {
      const res = await submitTelegramPassword(token, { password: twoFactorPassword });
      if (res.success) {
        setShowAuthDialog(false);
        resetOtpState();
        toast({
          title: "Telegram Connected",
          description: "2FA authentication completed successfully!",
          variant: "success",
        });
        loadStatus();
        loadSources();
      } else {
        setOtpError(res.message || "Incorrect password.");
      }
    } catch (err) {
      setOtpError(err instanceof Error ? err.message : "Password verification failed.");
    } finally {
      setOtpLoading(false);
    }
  };

  const resetOtpState = () => {
    setOtpStep(1);
    setPhoneNumber("");
    setPhoneCodeHash("");
    setVerificationCode("");
    setTwoFactorPassword("");
    setOtpError(null);
  };

  const handleLogout = async () => {
    if (!token) return;
    try {
      setLoggingOut(true);
      await logoutTelegram(token);
      setQrSession(null);
      toast({
        title: "Logged Out",
        description: "Telegram account disconnected successfully.",
        variant: "success",
      });
      loadStatus();
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

  // Sync Telegram Action
  const handleSyncTelegram = async () => {
    if (!token) return;
    try {
      setSyncing(true);
      const result = await syncTelegramSources(token);
      setSyncResult(result);
      setShowSyncModal(true);
      toast({
        title: "Sync Completed",
        description: `Discovered ${result.discoveredCount} chats (${result.newCount} new, ${result.updatedCount} updated).`,
        variant: "success",
      });
      await loadSources();
    } catch (err) {
      toast({
        title: "Synchronization Failed",
        description: err instanceof Error ? err.message : "Failed to sync Telegram dialogs",
        variant: "error",
      });
    } finally {
      setSyncing(false);
    }
  };

  // Toggle Capability directly on card/row
  const handleToggleCapability = async (
    source: TelegramSourceDto,
    key: "isEnabled" | "listenForSignals" | "processMessages",
    value: boolean
  ) => {
    if (!token) return;

    // Optimistic Update
    setSources((prev) =>
      prev.map((s) => (s.id === source.id ? { ...s, [key]: value } : s))
    );

    try {
      await updateTelegramSource(token, source.id, { [key]: value });
      toast({
        title: "Source Updated",
        description: `'${source.title}' updated successfully.`,
        variant: "success",
      });
      await loadSources();
    } catch (err) {
      // Revert Optimistic Update
      setSources((prev) =>
        prev.map((s) => (s.id === source.id ? { ...s, [key]: !value } : s))
      );
      toast({
        title: "Update Failed",
        description: err instanceof Error ? err.message : "Failed to update source",
        variant: "error",
      });
    }
  };

  // Delete Source Action
  const handleDeleteSource = async (source: TelegramSourceDto) => {
    if (!token) return;
    if (!confirm(`Are you sure you want to delete source '${source.title}'?`)) return;

    try {
      await deleteTelegramSource(token, source.id);
      toast({
        title: "Source Deleted",
        description: `'${source.title}' was deleted successfully.`,
        variant: "success",
      });
      if (activeSource?.id === source.id) {
        setActiveSource(null);
      }
      await loadSources();
    } catch (err) {
      toast({
        title: "Delete Failed",
        description: err instanceof Error ? err.message : "Failed to delete source",
        variant: "error",
      });
    }
  };

  // Bulk Actions
  const handleBulkAction = async (action: string, minutes?: number) => {
    if (!token || selectedIds.length === 0) return;

    try {
      setBulkActionLoading(true);
      const res = await bulkUpdateSources(token, {
        sourceIds: selectedIds,
        action,
        pauseMinutes: minutes,
      });
      toast({
        title: "Bulk Action Executed",
        description: `Updated ${res.updatedCount} sources successfully.`,
        variant: "success",
      });
      setSelectedIds([]);
      await loadSources();
    } catch (err) {
      toast({
        title: "Bulk Action Failed",
        description: err instanceof Error ? err.message : "Failed to execute bulk action",
        variant: "error",
      });
    } finally {
      setBulkActionLoading(false);
    }
  };

  // Selection Checkboxes
  const toggleSelectAll = () => {
    if (selectedIds.length === sources.length) {
      setSelectedIds([]);
    } else {
      setSelectedIds(sources.map((s) => s.id));
    }
  };

  const toggleSelectOne = (id: string) => {
    setSelectedIds((prev) =>
      prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]
    );
  };

  // Source Details & Diagnostics
  const handleOpenDetails = async (source: TelegramSourceDto) => {
    setActiveSource(source);
    setDetailsTab("overview");
    setTestResult(null);
    setMsgPage(1);
    setSigPage(1);

    if (!token) return;

    try {
      setLoadingDetails(true);
      const [msgs, sigs, h] = await Promise.all([
        fetchSourceMessages(token, source.id, 1, 20),
        fetchSourceSignals(token, source.id, 1, 20),
        fetchSourceHealth(token, source.id),
      ]);
      setMessages(msgs || []);
      setSignals(sigs || []);
      setHealth(h);
    } catch (err) {
      console.error("Failed to load source details", err);
    } finally {
      setLoadingDetails(false);
    }
  };

  const handlePageChangeMessages = async (p: number) => {
    if (!token || !activeSource) return;
    setMsgPage(p);
    const msgs = await fetchSourceMessages(token, activeSource.id, p, 20);
    setMessages(msgs || []);
  };

  const handlePageChangeSignals = async (p: number) => {
    if (!token || !activeSource) return;
    setSigPage(p);
    const sigs = await fetchSourceSignals(token, activeSource.id, p, 20);
    setSignals(sigs || []);
  };

  const handleTestSource = async (id: string) => {
    if (!token) return;
    try {
      setTestingSource(true);
      const res = await testTelegramSource(token, id);
      setTestResult(res);
      toast({
        title: res.success ? "Test Passed" : "Test Issues Detected",
        description: res.message,
        variant: res.success ? "success" : "error",
      });
    } catch (err) {
      toast({
        title: "Test Execution Failed",
        description: err instanceof Error ? err.message : "Failed to test source",
        variant: "error",
      });
    } finally {
      setTestingSource(false);
    }
  };

  // Overview Metrics
  const metrics = useMemo(() => {
    const isConnected = status?.connected || status?.status === "Active" || status?.status === "Connected" || status?.status === "Listening";
    const total = sources.length;
    const active = sources.filter((s) => s.status === "Listening").length;
    const paused = sources.filter((s) => s.status === "Paused").length;
    const disabled = sources.filter((s) => s.status === "Disabled").length;
    const errors = sources.filter((s) => s.status === "Error").length;

    return {
      isConnected,
      total,
      active,
      paused,
      disabled,
      errors,
    };
  }, [status, sources]);

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="Telegram Control Center"
        description="Centralized management of Telegram Signal Channels, Groups, and Listener Capabilities."
        actions={
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={loadSources} disabled={loadingSources}>
              <RefreshCw className={`mr-2 h-4 w-4 ${loadingSources ? "animate-spin" : ""}`} />
              Refresh
            </Button>

            <Button size="sm" onClick={handleSyncTelegram} disabled={syncing}>
              {syncing ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Syncing Telegram...
                </>
              ) : (
                <>
                  <Radio className="mr-2 h-4 w-4 text-emerald-400" />
                  Sync Telegram
                </>
              )}
            </Button>
          </div>
        }
      />

      {/* Overview Cards */}
      <div className="grid gap-4 md:grid-cols-4">
        <Card className="bg-card">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs text-muted-foreground uppercase font-medium">Connection</p>
              <p className="text-lg font-bold mt-1 flex items-center gap-2">
                {metrics.isConnected ? (
                  <span className="text-emerald-500 flex items-center gap-1.5">
                    <CheckCircle2 className="h-4 w-4" /> Connected
                  </span>
                ) : (
                  <span className="text-amber-500 flex items-center gap-1.5">
                    <XCircle className="h-4 w-4" /> Not Connected
                  </span>
                )}
              </p>
            </div>
            <div className="p-2.5 bg-primary/10 text-primary rounded-lg">
              <Send className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>

        <Card className="bg-card">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs text-muted-foreground uppercase font-medium">Active Sources</p>
              <p className="text-xl font-bold mt-1 text-foreground">{metrics.active} / {metrics.total}</p>
            </div>
            <div className="p-2.5 bg-emerald-500/10 text-emerald-500 rounded-lg">
              <Activity className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>

        <Card className="bg-card">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs text-muted-foreground uppercase font-medium">Paused Sources</p>
              <p className="text-xl font-bold mt-1 text-amber-500">{metrics.paused}</p>
            </div>
            <div className="p-2.5 bg-amber-500/10 text-amber-500 rounded-lg">
              <Pause className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>

        <Card className="bg-card">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs text-muted-foreground uppercase font-medium">Errors / Disabled</p>
              <p className="text-xl font-bold mt-1 text-muted-foreground">
                {metrics.errors} / {metrics.disabled}
              </p>
            </div>
            <div className="p-2.5 bg-muted/20 text-muted-foreground rounded-lg">
              <AlertTriangle className="h-5 w-5" />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Account Auth Card if Disconnected */}
      {!metrics.isConnected && (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardHeader className="pb-3">
            <div className="flex items-center justify-between">
              <CardTitle className="text-base flex items-center gap-2 text-amber-500">
                <ShieldCheck className="h-5 w-5" /> Telegram Client Authentication Required
              </CardTitle>
              <Badge variant="outline" className="border-amber-500/50 text-amber-500">
                Action Required
              </Badge>
            </div>
            <CardDescription>
              Connect your Telegram account using QR Login or Phone OTP to enable active channel monitoring and signal listening.
            </CardDescription>
          </CardHeader>

          <CardContent className="pt-0">
            <div className="flex items-center justify-between">
              <p className="text-xs text-muted-foreground">
                Authenticate your Telegram account using QR Code scan or Phone Number OTP code.
              </p>
              <Button size="sm" onClick={() => setShowAuthDialog(true)}>
                <ShieldCheck className="mr-2 h-4 w-4" /> Connect Telegram Account
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Authentication Dialog (QR + OTP) */}
      <Dialog open={showAuthDialog} onOpenChange={setShowAuthDialog}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" /> Telegram Authentication
            </DialogTitle>
            <DialogDescription>
              Select your preferred authentication method to link your Telegram account.
            </DialogDescription>
          </DialogHeader>

          <Tabs value={authMethod} onValueChange={(val) => setAuthMethod(val as "qr" | "otp")} className="w-full">
            <TabsList className="grid grid-cols-2 w-full mb-4">
              <TabsTrigger value="qr" className="flex items-center gap-2">
                <QrCode className="h-4 w-4" /> QR Code Login
              </TabsTrigger>
              <TabsTrigger value="otp" className="flex items-center gap-2">
                <User className="h-4 w-4" /> Phone OTP Login
              </TabsTrigger>
            </TabsList>

            {/* QR Code Tab */}
            <TabsContent value="qr" className="space-y-4">
              {qrSession?.qrData ? (
                <div className="flex flex-col items-center gap-4 p-4 bg-muted/20 rounded-lg border border-border text-center">
                  <div className="p-3 bg-white rounded-lg shadow-sm">
                    <QRCodeSVG value={qrSession.qrData} size={180} level="M" />
                  </div>
                  <div className="space-y-1">
                    <p className="font-semibold text-foreground text-sm flex items-center justify-center gap-2">
                      <Loader2 className="h-4 w-4 animate-spin text-primary" /> Waiting for Telegram Scan...
                    </p>
                    <p className="text-xs text-muted-foreground">
                      Open Telegram on your phone → Settings → Devices → Link Desktop Device
                    </p>
                  </div>
                  <Button size="sm" variant="outline" onClick={() => setQrSession(null)} className="text-xs">
                    Cancel Session
                  </Button>
                </div>
              ) : (
                <div className="space-y-4 py-2">
                  <p className="text-xs text-muted-foreground">
                    Scanning the QR code allows fast authorization without receiving SMS or app push codes.
                  </p>
                  <Button size="sm" onClick={handleStartQr} disabled={startingQr} className="w-full">
                    {startingQr ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <QrCode className="mr-2 h-4 w-4" />}
                    Generate QR Code
                  </Button>
                </div>
              )}
            </TabsContent>

            {/* Phone OTP Tab */}
            <TabsContent value="otp" className="space-y-4">
              {otpError && (
                <div className="p-3 bg-destructive/10 border border-destructive/20 rounded-md flex items-center gap-2 text-destructive text-xs font-medium">
                  <AlertTriangle className="h-4 w-4 shrink-0" />
                  <span>{otpError}</span>
                </div>
              )}

              {otpStep === 1 && (
                <form onSubmit={handleStartOtp} className="space-y-3">
                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-foreground">Phone Number</label>
                    <Input
                      type="tel"
                      placeholder="+1234567890"
                      value={phoneNumber}
                      onChange={(e) => setPhoneNumber(e.target.value)}
                      disabled={otpLoading}
                      required
                    />
                    <p className="text-[11px] text-muted-foreground">
                      Enter full phone number in international format (including country code).
                    </p>
                  </div>

                  <Button type="submit" size="sm" disabled={otpLoading || !phoneNumber.trim()} className="w-full">
                    {otpLoading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Send className="mr-2 h-4 w-4" />}
                    Send Verification Code
                  </Button>
                </form>
              )}

              {otpStep === 2 && (
                <form onSubmit={handleVerifyOtp} className="space-y-3">
                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-foreground">Verification Code</label>
                    <Input
                      type="text"
                      placeholder="12345"
                      value={verificationCode}
                      onChange={(e) => setVerificationCode(e.target.value)}
                      disabled={otpLoading}
                      required
                      autoFocus
                    />
                    <p className="text-[11px] text-muted-foreground">
                      Enter the login code sent to your Telegram app or phone.
                    </p>
                  </div>

                  <div className="flex items-center justify-between gap-2">
                    <Button type="button" variant="outline" size="sm" onClick={() => setOtpStep(1)} disabled={otpLoading}>
                      Back
                    </Button>
                    <Button type="submit" size="sm" disabled={otpLoading || !verificationCode.trim()}>
                      {otpLoading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : "Verify Code"}
                    </Button>
                  </div>
                </form>
              )}

              {otpStep === 3 && (
                <form onSubmit={handleSubmitPassword} className="space-y-3">
                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-foreground">Two-Step Verification Password</label>
                    <Input
                      type="password"
                      placeholder="Enter 2FA Password"
                      value={twoFactorPassword}
                      onChange={(e) => setTwoFactorPassword(e.target.value)}
                      disabled={otpLoading}
                      required
                      autoFocus
                    />
                    <p className="text-[11px] text-muted-foreground">
                      Your Telegram account has Two-Step Verification enabled. Enter your password to complete login.
                    </p>
                  </div>

                  <div className="flex items-center justify-between gap-2">
                    <Button type="button" variant="outline" size="sm" onClick={() => setOtpStep(2)} disabled={otpLoading}>
                      Back
                    </Button>
                    <Button type="submit" size="sm" disabled={otpLoading || !twoFactorPassword}>
                      {otpLoading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : "Submit Password"}
                    </Button>
                  </div>
                </form>
              )}
            </TabsContent>
          </Tabs>

          <DialogFooter>
            <Button variant="ghost" size="sm" onClick={() => setShowAuthDialog(false)}>
              Close
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Bulk Action Bar when selection exists */}
      {selectedIds.length > 0 && (
        <div className="flex flex-wrap items-center justify-between gap-3 p-3 bg-primary/10 border border-primary/30 rounded-lg">
          <div className="flex items-center gap-2 text-sm font-medium text-primary">
            <Check className="h-4 w-4" />
            <span>{selectedIds.length} source(s) selected</span>
          </div>

          <div className="flex items-center gap-2">
            <Button size="sm" variant="outline" onClick={() => handleBulkAction("Enable")} disabled={bulkActionLoading}>
              <Play className="mr-1.5 h-3.5 w-3.5 text-emerald-500" /> Enable
            </Button>

            <Button size="sm" variant="outline" onClick={() => handleBulkAction("Disable")} disabled={bulkActionLoading}>
              <Power className="mr-1.5 h-3.5 w-3.5 text-destructive" /> Disable
            </Button>

            <Button size="sm" variant="outline" onClick={() => handleBulkAction("EnableSignals")} disabled={bulkActionLoading}>
              <Radio className="mr-1.5 h-3.5 w-3.5 text-primary" /> Enable Signals
            </Button>

            <Button size="sm" variant="outline" onClick={() => handleBulkAction("DisableSignals")} disabled={bulkActionLoading}>
              <Radio className="mr-1.5 h-3.5 w-3.5 text-muted-foreground" /> Disable Signals
            </Button>

            <Button size="sm" variant="outline" onClick={() => handleBulkAction("Pause", 60)} disabled={bulkActionLoading}>
              <Clock className="mr-1.5 h-3.5 w-3.5 text-amber-500" /> Pause 1h
            </Button>

            <Button size="sm" variant="ghost" onClick={() => setSelectedIds([])} className="text-xs">
              Deselect All
            </Button>
          </div>
        </div>
      )}

      {/* Filter & Control Toolbar */}
      <Card>
        <CardContent className="p-4 space-y-4">
          <div className="flex flex-col sm:flex-row items-center justify-between gap-4">
            {/* Search Input */}
            <div className="relative w-full sm:w-80">
              <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Search by Title or Username..."
                className="pl-9"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>

            {/* Dropdown Filters */}
            <div className="flex items-center gap-3 w-full sm:w-auto">
              <Select value={selectedType} onValueChange={setSelectedType}>
                <SelectTrigger className="w-[140px]">
                  <SelectValue placeholder="Type" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="All">All Types</SelectItem>
                  <SelectItem value="Channel">Channel</SelectItem>
                  <SelectItem value="Group">Group</SelectItem>
                  <SelectItem value="Supergroup">Supergroup</SelectItem>
                </SelectContent>
              </Select>

              <Select value={selectedStatus} onValueChange={setSelectedStatus}>
                <SelectTrigger className="w-[140px]">
                  <SelectValue placeholder="Status" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="All">All Statuses</SelectItem>
                  <SelectItem value="Listening">Listening</SelectItem>
                  <SelectItem value="Paused">Paused</SelectItem>
                  <SelectItem value="Disabled">Disabled</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Sources Data Table */}
      <Card>
        <CardContent className="p-0">
          {loadingSources ? (
            <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
              <Loader2 className="h-5 w-5 animate-spin text-primary" />
              <span>Loading Telegram sources...</span>
            </div>
          ) : sources.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center text-muted-foreground space-y-3">
              <Radio className="h-12 w-12 text-muted-foreground/30 stroke-[1.5]" />
              <p className="text-base font-semibold text-foreground">No Telegram Sources Found</p>
              <p className="text-xs max-w-sm">
                Click &quot;Sync Telegram&quot; to discover channels and groups accessible from your connected Telegram account.
              </p>
              <Button size="sm" onClick={handleSyncTelegram} disabled={syncing}>
                <Radio className="mr-2 h-4 w-4" /> Sync Telegram Dialogs
              </Button>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-12 text-center">
                    <input
                      type="checkbox"
                      checked={selectedIds.length === sources.length && sources.length > 0}
                      onChange={toggleSelectAll}
                      className="rounded border-border"
                    />
                  </TableHead>
                  <TableHead>Source Title & Username</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="text-center">Listen</TableHead>
                  <TableHead className="text-center">Signals</TableHead>
                  <TableHead className="text-center">Messages</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {sources.map((source) => {
                  const isSelected = selectedIds.includes(source.id);

                  return (
                    <TableRow key={source.id} className={isSelected ? "bg-primary/5" : ""}>
                      <TableCell className="text-center">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => toggleSelectOne(source.id)}
                          className="rounded border-border"
                        />
                      </TableCell>

                      <TableCell>
                        <div>
                          <p className="font-semibold text-sm text-foreground">{source.title}</p>
                          <p className="text-xs text-muted-foreground font-mono">
                            {source.username || "No Username"}
                          </p>
                        </div>
                      </TableCell>

                      <TableCell>
                        <Badge variant="outline" className="text-[11px]">
                          {source.type}
                        </Badge>
                      </TableCell>

                      <TableCell>
                        {source.status === "Listening" ? (
                          <Badge className="bg-emerald-500/10 text-emerald-500 border-emerald-500/20">
                            Listening
                          </Badge>
                        ) : source.status === "Paused" ? (
                          <Badge className="bg-amber-500/10 text-amber-500 border-amber-500/20">
                            Paused
                          </Badge>
                        ) : (
                          <Badge variant="secondary">Disabled</Badge>
                        )}
                      </TableCell>

                      {/* IsEnabled Toggle */}
                      <TableCell className="text-center">
                        <Switch
                          checked={source.isEnabled}
                          onCheckedChange={(val) => handleToggleCapability(source, "isEnabled", val)}
                        />
                      </TableCell>

                      {/* ListenForSignals Toggle */}
                      <TableCell className="text-center">
                        <Switch
                          checked={source.listenForSignals}
                          disabled={!source.isEnabled}
                          onCheckedChange={(val) => handleToggleCapability(source, "listenForSignals", val)}
                        />
                      </TableCell>

                      {/* ProcessMessages Toggle */}
                      <TableCell className="text-center">
                        <Switch
                          checked={source.processMessages}
                          disabled={!source.isEnabled}
                          onCheckedChange={(val) => handleToggleCapability(source, "processMessages", val)}
                        />
                      </TableCell>

                      {/* Quick Actions */}
                      <TableCell className="text-right">
                        <div className="flex items-center justify-end gap-1">
                          <Button size="sm" variant="ghost" onClick={() => handleOpenDetails(source)}>
                            <FileText className="h-4 w-4" />
                          </Button>

                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => handleTestSource(source.id)}
                            disabled={testingSource}
                          >
                            <Shield className="h-4 w-4 text-primary" />
                          </Button>

                          <Button
                            size="sm"
                            variant="ghost"
                            className="text-destructive hover:bg-destructive/10"
                            onClick={() => handleDeleteSource(source)}
                          >
                            <Trash2 className="h-4 w-4" />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* Sync Result Summary Modal */}
      <Dialog open={showSyncModal} onOpenChange={setShowSyncModal}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Radio className="h-5 w-5 text-emerald-500" /> Telegram Discovery Sync Summary
            </DialogTitle>
            <DialogDescription>
              Synchronization completed successfully with your connected Telegram account.
            </DialogDescription>
          </DialogHeader>

          {syncResult && (
            <div className="space-y-4 py-3">
              <div className="grid grid-cols-3 gap-2 text-center">
                <div className="p-3 bg-muted/30 rounded-lg border">
                  <p className="text-xs text-muted-foreground">Discovered</p>
                  <p className="text-lg font-bold mt-1 text-foreground">{syncResult.discoveredCount}</p>
                </div>
                <div className="p-3 bg-emerald-500/10 rounded-lg border border-emerald-500/20">
                  <p className="text-xs text-emerald-500 font-medium">New</p>
                  <p className="text-lg font-bold mt-1 text-emerald-500">{syncResult.newCount}</p>
                </div>
                <div className="p-3 bg-primary/10 rounded-lg border border-primary/20">
                  <p className="text-xs text-primary font-medium">Updated</p>
                  <p className="text-lg font-bold mt-1 text-primary">{syncResult.updatedCount}</p>
                </div>
              </div>

              {syncResult.discoveredTitles.length > 0 && (
                <div className="space-y-1.5">
                  <p className="text-xs font-semibold text-muted-foreground uppercase">Discovered Sources:</p>
                  <div className="max-h-36 overflow-y-auto border rounded-md p-2 space-y-1 bg-muted/10">
                    {syncResult.discoveredTitles.map((t, i) => (
                      <p key={i} className="text-xs text-foreground font-medium flex items-center gap-2">
                        <Check className="h-3 w-3 text-emerald-500" /> {t}
                      </p>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}

          <DialogFooter>
            <Button onClick={() => setShowSyncModal(false)} className="w-full">
              Done
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Source Details & Diagnostics Modal */}
      <Dialog open={!!activeSource} onOpenChange={(open) => !open && setActiveSource(null)}>
        <DialogContent className="sm:max-w-3xl max-h-[85vh] overflow-y-auto">
          {activeSource && (
            <>
              <DialogHeader>
                <div className="flex items-center justify-between">
                  <div>
                    <DialogTitle className="text-xl flex items-center gap-2">
                      <Radio className="h-5 w-5 text-primary" /> {activeSource.title}
                    </DialogTitle>
                    <DialogDescription className="font-mono text-xs mt-1">
                      {activeSource.username || "No Username"}
                    </DialogDescription>
                  </div>
                  <Badge variant={activeSource.isEnabled ? "default" : "secondary"}>
                    {activeSource.status}
                  </Badge>
                </div>
              </DialogHeader>

              <Tabs value={detailsTab} onValueChange={setDetailsTab} className="mt-2">
                <TabsList className="grid grid-cols-5 w-full">
                  <TabsTrigger value="overview">Overview</TabsTrigger>
                  <TabsTrigger value="config">Capabilities</TabsTrigger>
                  <TabsTrigger value="messages">Messages</TabsTrigger>
                  <TabsTrigger value="signals">Signals</TabsTrigger>
                  <TabsTrigger value="health">Health & Test</TabsTrigger>
                </TabsList>

                {/* Tab: Overview */}
                <TabsContent value="overview" className="space-y-4 pt-4">
                  <div className="grid grid-cols-2 gap-4 text-sm">
                    <div className="p-3 bg-muted/20 border rounded-lg space-y-1">
                      <span className="text-xs text-muted-foreground uppercase font-medium">Type</span>
                      <p className="font-semibold text-foreground">{activeSource.type}</p>
                    </div>

                    <div className="p-3 bg-muted/20 border rounded-lg space-y-1">
                      <span className="text-xs text-muted-foreground uppercase font-medium">Status</span>
                      <p className="font-semibold text-foreground">{activeSource.status}</p>
                    </div>

                    <div className="p-3 bg-muted/20 border rounded-lg space-y-1">
                      <span className="text-xs text-muted-foreground uppercase font-medium">Signal Listening</span>
                      <p className="font-semibold text-foreground">
                        {activeSource.listenForSignals ? "Enabled" : "Disabled"}
                      </p>
                    </div>

                    <div className="p-3 bg-muted/20 border rounded-lg space-y-1">
                      <span className="text-xs text-muted-foreground uppercase font-medium">Message Processing</span>
                      <p className="font-semibold text-foreground">
                        {activeSource.processMessages ? "Enabled" : "Disabled"}
                      </p>
                    </div>
                  </div>

                  <div className="p-4 border rounded-lg bg-card space-y-2">
                    <p className="text-xs font-semibold text-muted-foreground uppercase">Advanced Details</p>
                    <div className="grid grid-cols-2 gap-2 text-xs font-mono">
                      <div>
                        <span className="text-muted-foreground">Chat ID:</span> {activeSource.telegramChatId}
                      </div>
                      <div>
                        <span className="text-muted-foreground">Source ID:</span> {activeSource.id}
                      </div>
                      <div>
                        <span className="text-muted-foreground">Created At:</span>{" "}
                        {new Date(activeSource.createdAt).toLocaleString()}
                      </div>
                      <div>
                        <span className="text-muted-foreground">Updated At:</span>{" "}
                        {activeSource.updatedAt ? new Date(activeSource.updatedAt).toLocaleString() : "Never"}
                      </div>
                    </div>
                  </div>
                </TabsContent>

                {/* Tab: Capabilities & Configuration */}
                <TabsContent value="config" className="space-y-4 pt-4">
                  <div className="space-y-4 border rounded-lg p-4 bg-card">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="font-semibold text-sm">Source Listener</p>
                        <p className="text-xs text-muted-foreground">Enable or disable total listener ingestion for this source.</p>
                      </div>
                      <Switch
                        checked={activeSource.isEnabled}
                        onCheckedChange={(val) => {
                          handleToggleCapability(activeSource, "isEnabled", val);
                          setActiveSource((prev) => (prev ? { ...prev, isEnabled: val } : null));
                        }}
                      />
                    </div>

                    <div className="flex items-center justify-between border-t pt-3">
                      <div>
                        <p className="font-semibold text-sm">Signal Intelligence Listening</p>
                        <p className="text-xs text-muted-foreground">Forward messages from this source to Signal Detection engine.</p>
                      </div>
                      <Switch
                        checked={activeSource.listenForSignals}
                        disabled={!activeSource.isEnabled}
                        onCheckedChange={(val) => {
                          handleToggleCapability(activeSource, "listenForSignals", val);
                          setActiveSource((prev) => (prev ? { ...prev, listenForSignals: val } : null));
                        }}
                      />
                    </div>

                    <div className="flex items-center justify-between border-t pt-3">
                      <div>
                        <p className="font-semibold text-sm">Message Processing & Storage</p>
                        <p className="text-xs text-muted-foreground">Store messages in TelegramMessages database history.</p>
                      </div>
                      <Switch
                        checked={activeSource.processMessages}
                        disabled={!activeSource.isEnabled}
                        onCheckedChange={(val) => {
                          handleToggleCapability(activeSource, "processMessages", val);
                          setActiveSource((prev) => (prev ? { ...prev, processMessages: val } : null));
                        }}
                      />
                    </div>
                  </div>

                  {/* Pause Configuration */}
                  <div className="p-4 border rounded-lg bg-card space-y-3">
                    <p className="font-semibold text-sm flex items-center gap-2">
                      <Clock className="h-4 w-4 text-amber-500" /> Temporary Pause Controls
                    </p>
                    <p className="text-xs text-muted-foreground">
                      Pause listening temporarily without losing configuration options.
                    </p>

                    <div className="flex items-center gap-2 pt-2">
                      <Button size="sm" variant="outline" onClick={() => handleBulkAction("Pause", 60)}>
                        Pause 1 Hour
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => handleBulkAction("Pause", 360)}>
                        Pause 6 Hours
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => handleBulkAction("Pause", 1440)}>
                        Pause 24 Hours
                      </Button>
                      {activeSource.status === "Paused" && (
                        <Button size="sm" variant="default" onClick={() => handleBulkAction("Enable")}>
                          Resume Listening
                        </Button>
                      )}
                    </div>
                  </div>
                </TabsContent>

                {/* Tab: Recent Messages */}
                <TabsContent value="messages" className="space-y-4 pt-4">
                  {messages.length === 0 ? (
                    <div className="text-center py-8 text-muted-foreground text-xs">
                      No recent messages recorded for this channel.
                    </div>
                  ) : (
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Msg ID</TableHead>
                          <TableHead>Preview</TableHead>
                          <TableHead>Received At</TableHead>
                          <TableHead>Processed</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {messages.map((m) => (
                          <TableRow key={m.id}>
                            <TableCell className="font-mono text-xs">{m.messageId}</TableCell>
                            <TableCell className="text-xs font-mono">{m.preview}</TableCell>
                            <TableCell className="text-xs">
                              {new Date(m.receivedAt).toLocaleTimeString()}
                            </TableCell>
                            <TableCell>
                              <Badge variant={m.processed ? "default" : "outline"} className="text-[10px]">
                                {m.processed ? "Processed" : "Pending"}
                              </Badge>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  )}
                </TabsContent>

                {/* Tab: Signals */}
                <TabsContent value="signals" className="space-y-4 pt-4">
                  {signals.length === 0 ? (
                    <div className="text-center py-8 text-muted-foreground text-xs">
                      No signals detected for this source yet.
                    </div>
                  ) : (
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Symbol</TableHead>
                          <TableHead>Side</TableHead>
                          <TableHead>Confidence</TableHead>
                          <TableHead>Status</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {signals.map((s) => (
                          <TableRow key={s.id}>
                            <TableCell className="font-bold text-xs">{s.symbol}</TableCell>
                            <TableCell className="text-xs">{s.action}</TableCell>
                            <TableCell className="text-xs">{s.confidence}%</TableCell>
                            <TableCell>
                              <Badge className="text-[10px]">{s.status}</Badge>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  )}
                </TabsContent>

                {/* Tab: Health & Test Source */}
                <TabsContent value="health" className="space-y-4 pt-4">
                  <div className="p-4 border rounded-lg bg-card space-y-3">
                    <div className="flex items-center justify-between">
                      <p className="font-semibold text-sm flex items-center gap-2">
                        <Activity className="h-4 w-4 text-emerald-500" /> Source Health Status
                      </p>
                      <Button
                        size="sm"
                        onClick={() => handleTestSource(activeSource.id)}
                        disabled={testingSource}
                      >
                        {testingSource ? (
                          <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                        ) : (
                          <Shield className="mr-2 h-4 w-4" />
                        )}
                        Run Diagnostic Test
                      </Button>
                    </div>

                    {health && (
                      <div className="grid grid-cols-2 gap-3 text-xs pt-2">
                        <div className="p-2.5 bg-muted/20 rounded-md">
                          <span className="text-muted-foreground">Connection Status:</span>{" "}
                          <span className="font-semibold text-foreground">{health.connectionStatus}</span>
                        </div>
                        <div className="p-2.5 bg-muted/20 rounded-md">
                          <span className="text-muted-foreground">Listener State:</span>{" "}
                          <span className="font-semibold text-foreground">{health.listenerState}</span>
                        </div>
                      </div>
                    )}
                  </div>

                  {/* Diagnostic Test Output Breakdown */}
                  {testResult && (
                    <div className="p-4 border rounded-lg bg-card space-y-3">
                      <p className="font-semibold text-sm flex items-center gap-2">
                        {testResult.success ? (
                          <CheckCircle2 className="h-5 w-5 text-emerald-500" />
                        ) : (
                          <XCircle className="h-5 w-5 text-destructive" />
                        )}
                        Diagnostic Test Result
                      </p>

                      <p className="text-xs font-medium text-foreground">{testResult.message}</p>

                      <div className="space-y-1.5 pt-2 border-t">
                        {testResult.details.map((d, i) => (
                          <p key={i} className="text-xs text-muted-foreground font-mono flex items-center gap-2">
                            <Info className="h-3 w-3 text-primary shrink-0" /> {d}
                          </p>
                        ))}
                      </div>
                    </div>
                  )}
                </TabsContent>
              </Tabs>
            </>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
