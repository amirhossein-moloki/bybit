# Resilience Strategy

This document details the reliability, fault tolerance, and failure handling strategy implemented inside the Telegram Signal Trading Bot.

## Polly Integration

We use the industry-standard `Polly` resilience library (version 8.0.0 options-based API) inside the `TradingBot.Infrastructure` project, acting as a buffer between the Application and Exchange boundaries.

## Implemented Resilience Pipelines

Two distinct pipelines have been configured inside the `ResilienceService` to cover REST and WebSocket channels:

### 1. HTTP REST Resilience Pipeline

The REST pipeline wraps HTTP requests (e.g. order placement, order query, ping checks) inside a cohesive resilience wrap:

#### Timeout Policy
- **Threshold**: 10 seconds.
- **Action**: Limits execution of long-running or hung HTTP requests, aborting and triggering a timeout exception which can be retried.

#### Retry Policy
- **Conditions**: Retries on transient HTTP network issues (`HttpRequestException`) or rate limit errors (HTTP 429).
- **Attempts**: 3 attempts.
- **Backoff**: Exponential backoff with random jitter (base delay of 2 seconds), scaling delay to prevent hitting exchange rate limits.
- **Excluded**: Critical/Auth errors (e.g., API key/secret/signature validation failures) are **never** retried.

#### Circuit Breaker Policy
- **Threshold**: Opens if 50% of the last requests within a 10-second sampling window fail.
- **Minimum Throughput**: 4 requests (prevents premature opening on low volume).
- **Break Duration**: 15 seconds.
- **Action**: Blocks outgoing REST requests immediately while open, preventing exchange overload and letting external services heal.

---

### 2. WebSocket Resilience Pipeline

The WebSocket pipeline provides stability over real-time connections:

#### Timeout Policy
- **Threshold**: 15 seconds.
- **Action**: Safeguards the initial socket connection handshake from blocking indefinitely.

#### Retry Policy
- **Conditions**: Retries on any socket exception or connection handshake failure.
- **Attempts**: 5 attempts.
- **Backoff**: Exponential backoff with jitter (base delay of 1 second).

---

## Rate Limit Handling

Bybit rate limit responses (HTTP 429 / Too Many Requests) are handled gracefully:
- Detected via HTTP status codes or error messages.
- The HTTP pipeline intercepts them and applies an exponential backoff with jitter, delaying subsequent calls to allow the rate limit bucket to reset.
- Detailed warning logs are captured with structured metadata (delay duration, attempt number).
