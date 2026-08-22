export interface TelegramAccountDto {
  id: number;
  username?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  phone?: string | null;
}

export interface TelegramStatusDto {
  connected: boolean;
  status: string;
  account?: TelegramAccountDto | null;
}

export interface TelegramQrStartResultDto {
  sessionId: string;
  qrData: string;
  expiresAt: string;
}

export interface TelegramQrStatusDto {
  sessionId: string;
  status: "WaitingForScan" | "ScanDetected" | "Authenticating" | "Connected" | "Expired" | "Failed" | string;
  qrData?: string | null;
  expiresAt?: string | null;
  account?: TelegramAccountDto | null;
  error?: string | null;
}

export interface OtpStartResultDto {
  success: boolean;
  phoneCodeHash?: string | null;
  message?: string | null;
  error?: string | null;
}

export interface OtpVerifyResultDto {
  success: boolean;
  status?: string | null;
  requiresPassword?: boolean;
  error?: string | null;
}

export interface PasswordResultDto {
  success: boolean;
  status?: string | null;
  error?: string | null;
}

export interface TelegramDialogDto {
  id: number;
  title: string;
  username: string;
  isChannel: boolean;
  isGroup: boolean;
  isMonitored: boolean;
}

export interface TelegramSourceDto {
  id: string;
  telegramChatId: number;
  title: string;
  username?: string | null;
  type: "Channel" | "Group" | "Supergroup" | string;
  isEnabled: boolean;
  listenForSignals: boolean;
  processMessages: boolean;
  pausedUntil?: string | null;
  status: "Listening" | "Paused" | "Disabled" | "Connecting" | "Error" | string;
  lastMessageAt?: string | null;
  messagesToday: number;
  signalsToday: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface UpdateTelegramSourceDto {
  isEnabled?: boolean | null;
  listenForSignals?: boolean | null;
  processMessages?: boolean | null;
  pauseMinutes?: number | null;
}

export interface TelegramSourceFilter {
  search?: string;
  type?: string;
  isEnabled?: boolean;
  listenForSignals?: boolean;
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface SyncSourcesResultDto {
  discoveredCount: number;
  newCount: number;
  updatedCount: number;
  errorCount: number;
  discoveredTitles: string[];
}

export interface TestSourceResultDto {
  success: boolean;
  telegramConnected: boolean;
  sourceAccessible: boolean;
  messagesReadable: boolean;
  listenerConfigured: boolean;
  signalProcessingAvailable: boolean;
  message: string;
  details: string[];
}

export interface BulkUpdateSourcesDto {
  sourceIds: string[];
  action: "Enable" | "Disable" | "EnableSignals" | "DisableSignals" | "Pause" | string;
  pauseMinutes?: number | null;
}

export interface TelegramMessagePreviewDto {
  id: string;
  messageId: number;
  senderId?: number | null;
  preview: string;
  receivedAt: string;
  processed: boolean;
}

export interface TelegramSignalPreviewDto {
  id: string;
  messageId: number;
  symbol: string;
  action: string;
  confidence: number;
  status: string;
  createdAt: string;
}

export interface TelegramSourceHealthDto {
  connectionStatus: string;
  listenerState: string;
  lastMessageAt?: string | null;
  lastSignalAt?: string | null;
  processingErrors: number;
  reconnectCount: number;
}
