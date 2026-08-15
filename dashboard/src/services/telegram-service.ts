import { apiGet, apiPost } from "@/lib/api-client";
import type { ApiSuccess } from "@/types/api";
import type {
  TelegramStatusDto,
  TelegramQrStartResultDto,
  TelegramQrStatusDto,
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

export async function logoutTelegram(token: string): Promise<{ message: string }> {
  return apiPost<ApiSuccess<{ message: string }>>(`${base}/auth/logout`, { token }).then(unwrap);
}
