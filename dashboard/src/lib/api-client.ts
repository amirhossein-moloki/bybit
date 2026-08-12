const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/+$/, "") ?? "";

export class ApiError extends Error {
  status: number;
  code: string;
  correlationId?: string;
  retryable: boolean;

  constructor(
    status: number,
    code: string,
    message: string,
    correlationId?: string
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.correlationId = correlationId;
    this.retryable =
      status === 429 || status === 500 || status === 502 || status === 503;
  }
}

export class NetworkError extends Error {
  retryable = true;
  constructor(message = "Network error") {
    super(message);
    this.name = "NetworkError";
  }
}

export class TimeoutError extends Error {
  retryable = true;
  constructor(message = "Request timed out") {
    super(message);
    this.name = "TimeoutError";
  }
}

interface RequestOptions {
  method?: "GET" | "POST";
  query?: Record<string, string | number | boolean | null | undefined> | object;
  body?: unknown;
  token?: string | null;
  timeoutMs?: number;
}

function buildUrl(path: string, query?: RequestOptions["query"]): string {
  const base = path.startsWith("/") ? path : `/${path}`;
  const url = new URL(`${API_BASE_URL}${base}`, "http://localhost");

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== "") {
        url.searchParams.set(key, String(value));
      }
    }
  }

  const full = `${url.pathname}${url.search}`;
  return API_BASE_URL ? `${API_BASE_URL}${full}` : full;
}

async function request<T>(
  path: string,
  options: RequestOptions = {}
): Promise<T> {
  const { method = "GET", query, body, token, timeoutMs = 20000 } = options;

  const headers: Record<string, string> = {
    Accept: "application/json",
  };

  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  let response: Response;
  try {
    response = await fetch(buildUrl(path, query), {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: controller.signal,
      cache: "no-store",
    });
  } catch (err) {
    clearTimeout(timer);
    if (err instanceof DOMException && err.name === "AbortError") {
      throw new TimeoutError(
        `Request to ${path} timed out after ${timeoutMs}ms`
      );
    }
    throw new NetworkError(`Unable to reach the API at ${path}`);
  }
  clearTimeout(timer);

  if (response.status === 204) {
    return undefined as T;
  }

  const correlationId =
    response.headers.get("X-Correlation-ID") ?? undefined;

  const contentType = response.headers.get("content-type") ?? "";

  if (contentType.includes("application/json")) {
    const payload = (await response.json()) as unknown;

    if (!response.ok) {
      const errorBody = payload as {
        status?: string;
        error?: { code?: string; message?: string; correlationId?: string };
      };
      throw new ApiError(
        response.status,
        errorBody?.error?.code ?? "UNKNOWN_ERROR",
        errorBody?.error?.message ?? friendlyError(response.status),
        errorBody?.error?.correlationId ?? correlationId
      );
    }

    return payload as T;
  }

  if (contentType.includes("text/csv") || contentType.includes("text/plain")) {
    if (!response.ok) {
      throw new ApiError(
        response.status,
        "EXPORT_ERROR",
        `Export failed with status ${response.status}`,
        correlationId
      );
    }
    return (await response.text()) as unknown as T;
  }

  if (!response.ok) {
    throw new ApiError(
      response.status,
      "UNKNOWN_ERROR",
      friendlyError(response.status),
      correlationId
    );
  }

  return (await response.text()) as unknown as T;
}

function friendlyError(status: number): string {
  switch (status) {
    case 400:
      return "The request was invalid.";
    case 401:
      return "Authentication failed. Your token is invalid or expired.";
    case 403:
      return "You do not have permission to perform this action.";
    case 404:
      return "The requested resource was not found.";
    case 409:
      return "The request conflicts with the current state of the resource.";
    case 429:
      return "Too many requests. Please try again later.";
    case 500:
      return "The server encountered an internal error.";
    default:
      return `Request failed with status ${status}.`;
  }
}

export async function apiGet<T>(
  path: string,
  options: Omit<RequestOptions, "method" | "body"> = {}
): Promise<T> {
  return request<T>(path, { ...options, method: "GET" });
}

export async function apiPost<T>(
  path: string,
  options: Omit<RequestOptions, "method"> = {}
): Promise<T> {
  return request<T>(path, { ...options, method: "POST" });
}

export { API_BASE_URL };
