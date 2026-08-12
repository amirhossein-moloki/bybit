# Trading Bot Dashboard — Final Report

## Goal
A production-ready, dark-first web dashboard for the existing .NET 8 trading bot, built from the real backend contracts (no invented APIs or mock data) and verified end-to-end against a live backend + PostgreSQL.

## Delivery
- **Dashboard**: `/workspace/dashboard` (Next.js 14.2.15, TypeScript strict, Tailwind, TanStack Query 5, Recharts, Radix UI, lucide-react).
- **Backend**: running on `http://localhost:5293` (Release, `--no-build`), DB at 32 tables with all 16 migrations applied.
- **Preview**: https://3000-dc3eb150a7735fa8.monkeycode-ai.live (dev server on port 3000, proxies `/api/*`, `/monitoring/*`, `/health/*` to the backend since the API has no CORS).

## Pages (all wired to real endpoints, polling 10–60s)
| Route | Source data | Key features |
|---|---|---|
| `/login` | `GET /api/dashboard/overview` (token validation) | Bearer token entry, show/hide, 401/403/network errors, no credentials stored server-side |
| `/overview` | `/api/dashboard/overview` | System/exchange/telegram health, order/position/trade summaries, PnL, account equity, balances |
| `/positions` | `/api/dashboard/positions`, `/trading` | Filters (symbol/side), pagination, live unrealized PnL with side-aware percent |
| `/orders` | `/api/dashboard/orders`, `/trading` | Status/side/symbol filters, pagination, live rows |
| `/trades` | `/api/dashboard/trades`, `/trading` | Close-reason badges, net PnL coloring, filters, pagination |
| `/performance` | `/api/analytics/*` | Equity curve, drawdown, monthly aggregation, long/short split charts, period presets |
| `/health` | `/api/dashboard/health` | Overall health badge, workers table, DB/Bybit REST+WS/Telegram/monitoring rows, operational metrics, active alerts, recent events, health history |
| `/alerts` | `/api/dashboard/alerts` | Severity/source/type filters, pagination, empty state |
| `/events` | `/api/dashboard/events` | Type/severity/source/date-range filters, pagination, correlation IDs |
| `/reports` | `/api/analytics/report`, `/export/csv` (GET), `/schedule` (POST) | Performance/drawdown/streak metrics, long-short split, CSV download, schedule-report dialog |

## Architecture decisions
- **Same-origin proxy**: `next.config.mjs` rewrites `/api/*` to `API_PROXY_TARGET` (default `http://localhost:5293`) when `NEXT_PUBLIC_API_URL` is unset; otherwise all requests go directly to that env var. No backend URLs hardcoded in source.
- **Auth**: single dashboard Bearer token (backend has no login endpoint). Stored in `localStorage`, attached as `Authorization: Bearer` by `api-client`. A global `tradingbot:unauthorized` event logs out on any 401/403. `hydrated` gate avoids SSR hydration mismatches.
- **Errors**: `ApiError`/`NetworkError`/`TimeoutError` with 400/401/403/404/409/429/500 mapping, Correlation ID surfaced in the UI, stack traces never exposed, sanitized messages only.
- **Real-time**: backend has no WebSocket/SSE, so TanStack Query polling (10–15s live pages, staleTime 5s, retry ≤ 2).
- **State handling**: skeletons, empty states with filter-aware copy, error states with retry, toast notifications for export/schedule actions.

## Verification performed
- `npm run typecheck` — clean. `npm run lint` — clean. `npm run build` — all 11 routes compile/prerender.
- Headless-browser (Playwright) tests against the live stack:
  - Unauthenticated redirect → `/login`; login page renders; wrong token shows error; valid token → `/overview`.
  - All 10 protected routes render their expected `h1` with live data; overview stat cards and 9-item sidebar present.
  - Logout → `/login`; mobile menu button present at 390px width.
  - `/alerts` empty state; `/events` 20-row table + pagination; `/health` workers + all sections.
  - `/reports`: stats render, CSV download fires (`trades-all.csv`), schedule dialog opens and POST succeeds with toast.
  - Error state: invalid date range returns backend `400` with message + Correlation ID + Retry; Reset recovers.
  - Proxy: overview/alerts/events/health/analytics return `200` with valid token, `401` with missing/bad token, `400` for validation errors.

## Backend fixes required (all applied)
1. Two orphaned signal migrations lacked `[DbContext]`/`[Migration]` attributes (no Designer files) → EF never discovered them. Attributes added; chain preserved.
2. Postgres `HasFilter` strings were unquoted (`Status != 'Resolved'`) → `42703` identifier errors. Fixed to `"Status"`/`"ExternalEventId"` in configs, snapshot, and Designer files.
3. Generated `20260812143800_AddAlertsAndAlertEvents` trimmed to only missing tables/indexes; first failed apply rolled back cleanly.

## How to run
Backend:
```bash
cd /workspace/src/TradingBot.Worker && ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://localhost:5293 \
  Database__ConnectionString="Host=localhost;Database=tradingbot;Username=postgres;Password=postgres" \
  Telegram__Enabled=false \
  Security__EncryptionKey="0123456789abcdef0123456789abcdef" \
  /usr/share/dotnet/dotnet run -c Release --no-build
```
Dashboard:
```bash
cd /workspace/dashboard && npm run dev
```
Open http://localhost:3000, sign in with the token `ValidDashboardReadToken`.

## Notes
- Two test report schedules exist in the local dev DB from verification (deactivated). Delete via the backend directly if unwanted.
- `NEXT_PUBLIC_API_URL` can override the proxy target for production; the backend has no CORS, so a same-origin proxy or direct env URL is required.
