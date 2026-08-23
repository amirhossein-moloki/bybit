import { apiGet, apiPost, apiPatch, apiDelete } from "@/lib/api-client";
import type { ApiSuccess } from "@/types/api";
import type {
  TelegramStatusDto,
  TelegramQrStartResultDto,
  TelegramQrStatusDto,
  OtpStartResultDto,
  OtpVerifyResultDto,
  PasswordResultDto,
  TelegramDialogDto,
  TelegramSourceDto,
  TelegramSourceFilter,
  UpdateTelegramSourceDto,
  SyncSourcesResultDto,
  TestSourceResultDto,
  BulkUpdateSourcesDto,
  TelegramMessagePreviewDto,
  TelegramSignalPreviewDto,
  TelegramSourceHealthDto,
} from "@/types/telegram";

const base = "/api/telegram";

function unwrap<T>(response: ApiSuccess<T>): T {
  return response.data;
}

export async function fetchTelegramStatus(token: string): Promise<TelegramStatusDto> {
  return apiGet<ApiSuccess<TelegramStatusDto>>(`${base}/status`, { token }).then(unwrap);
}

export async function startTelegramQrAuth(token: string): Promise<TelegramQrStartResultDto> {
  return apiPost<ApiSuccess<TelegramQrStartResultDto>>(`${base}/auth/qr/start`, { token }).then(unwrap);
}

export async function fetchTelegramQrStatus(token: string, sessionId: string): Promise<TelegramQrStatusDto> {
  return apiGet<ApiSuccess<TelegramQrStatusDto>>(`${base}/auth/qr/status`, {
    token,
    query: { sessionId },
  }).then(unwrap);
}

export async function startTelegramOtpAuth(token: string, phoneNumber: string): Promise<OtpStartResultDto> {
  return apiPost<OtpStartResultDto>(`${base}/auth/otp/start`, {
    token,
    body: { phoneNumber },
  });
}

export async function verifyTelegramOtp(
  token: string,
  phoneNumber: string,
  phoneCodeHash: string,
  code: string
): Promise<OtpVerifyResultDto> {
  return apiPost<OtpVerifyResultDto>(`${base}/auth/otp/verify`, {
    token,
    body: { phoneNumber, phoneCodeHash, code },
  });
}

export async function verifyTelegramPassword(token: string, password: string): Promise<PasswordResultDto> {
  return apiPost<PasswordResultDto>(`${base}/auth/password`, {
    token,
    body: { password },
  });
}

export async function logoutTelegram(token: string): Promise<{ message: string }> {
  return apiPost<ApiSuccess<{ message: string }>>(`${base}/auth/logout`, { token }).then(unwrap);
}

export async function fetchTelegramDialogs(token: string): Promise<TelegramDialogDto[]> {
  return apiGet<ApiSuccess<TelegramDialogDto[]>>(`${base}/dialogs`, { token }).then(unwrap);
}

export async function fetchMonitoredChannels(token: string): Promise<string[]> {
  return apiGet<ApiSuccess<string[]>>(`${base}/channels`, { token }).then(unwrap);
}

export async function toggleMonitoredChannel(
  token: string,
  identifier: string,
  enable: boolean
): Promise<{ identifier: string; enabled: boolean }> {
  return apiPost<ApiSuccess<{ identifier: string; enabled: boolean }>>(`${base}/channels/toggle`, {
    token,
    body: { identifier, enable },
  }).then(unwrap);
}

// Telegram Control Center — Source Management API Service Functions

export async function fetchTelegramSources(
  token: string,
  filter?: TelegramSourceFilter
): Promise<TelegramSourceDto[]> {
  return apiGet<ApiSuccess<TelegramSourceDto[]>>(`${base}/sources`, {
    token,
    query: filter as Record<string, string | number | boolean | null | undefined>,
  }).then(unwrap);
}

export async function fetchTelegramSourceById(
  token: string,
  id: string
): Promise<TelegramSourceDto> {
  return apiGet<ApiSuccess<TelegramSourceDto>>(`${base}/sources/${id}`, { token }).then(unwrap);
}

export async function updateTelegramSource(
  token: string,
  id: string,
  dto: UpdateTelegramSourceDto
): Promise<TelegramSourceDto> {
  return apiPatch<ApiSuccess<TelegramSourceDto>>(`${base}/sources/${id}`, {
    token,
    body: dto,
  }).then(unwrap);
}

export async function deleteTelegramSource(
  token: string,
  id: string
): Promise<{ message: string }> {
  return apiDelete<ApiSuccess<{ message: string }>>(`${base}/sources/${id}`, { token }).then(unwrap);
}

export async function syncTelegramSources(token: string): Promise<SyncSourcesResultDto> {
  return apiPost<ApiSuccess<SyncSourcesResultDto>>(`${base}/sources/sync`, { token }).then(unwrap);
}

export async function bulkUpdateSources(
  token: string,
  dto: BulkUpdateSourcesDto
): Promise<{ updatedCount: number }> {
  return apiPost<ApiSuccess<{ updatedCount: number }>>(`${base}/sources/bulk`, {
    token,
    body: dto,
  }).then(unwrap);
}

export async function fetchSourceMessages(
  token: string,
  id: string,
  page = 1,
  pageSize = 20
): Promise<TelegramMessagePreviewDto[]> {
  return apiGet<ApiSuccess<TelegramMessagePreviewDto[]>>(`${base}/sources/${id}/messages`, {
    token,
    query: { page, pageSize },
  }).then(unwrap);
}

export async function fetchSourceSignals(
  token: string,
  id: string,
  page = 1,
  pageSize = 20
): Promise<TelegramSignalPreviewDto[]> {
  return apiGet<ApiSuccess<TelegramSignalPreviewDto[]>>(`${base}/sources/${id}/signals`, {
    token,
    query: { page, pageSize },
  }).then(unwrap);
}

export async function fetchSourceHealth(
  token: string,
  id: string
): Promise<TelegramSourceHealthDto> {
  return apiGet<ApiSuccess<TelegramSourceHealthDto>>(`${base}/sources/${id}/health`, { token }).then(unwrap);
}

export async function testTelegramSource(
  token: string,
  id: string
): Promise<TestSourceResultDto> {
  return apiPost<ApiSuccess<TestSourceResultDto>>(`${base}/sources/${id}/test`, { token }).then(unwrap);
}
