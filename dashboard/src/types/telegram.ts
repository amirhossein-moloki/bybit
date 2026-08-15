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
