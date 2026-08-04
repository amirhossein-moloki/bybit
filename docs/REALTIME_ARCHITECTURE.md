# Real-Time Architecture

This document details the design and implementation of real-time communication within the Telegram Signal Trading Bot.

## Overview

To support enterprise-level high-frequency and resilient trading operations, we have introduced a reactive, decoupled, event-driven architecture rooted in Clean Architecture principles.

```
+-----------------------------------------------------------+
|                      TradingBot.Worker                    |
|  (ConnectionMonitorService, MarketDataBackgroundService,  |
|                 OrderSyncBackgroundService)               |
+-----------------------------------------------------------+
                             |
                             | Consumes
                             v
+-----------------------------------------------------------+
|                   TradingBot.Application                  |
|  Abstractions: IExchangeStreamClient, IMarketStream...    |
+-----------------------------------------------------------+
                             ^
                             | Implements
                             v
+-----------------------------------------------------------+
|                 TradingBot.Exchange.Bybit                 |
|   BybitWebSocketClient, MessageHandler, Streams, etc.     |
+-----------------------------------------------------------+
```

## Key Components

### 1. Application Layer Abstractions

All real-time components in the application consume abstract interfaces, protecting the domain and application layers from exchange-specific details:

- `IExchangeStreamClient`: The general coordinate interface for stream connectivity lifecycle (`ConnectAsync`, `DisconnectAsync`).
- `IMarketStream`: Handles market data stream operations (`SubscribeAsync`, `ReceiveEventsAsync`).
- `IOrderStream`: Processes private order event updates.
- `IPositionStream`: Processes real-time position adjustments.

### 2. Bybit WebSocket Client

Located inside `TradingBot.Exchange.Bybit/WebSocket/`, the client utilizes .NET's native `ClientWebSocket` to establish high-throughput, low-allocation full-duplex channels.

- **Dual Socket Connections**: It initiates two concurrent sockets:
  - **Public Socket**: To listen to market events (tickers).
  - **Private Socket**: To listen to private execution reports.
- **HMAC SHA-256 Authentication**: Private WebSocket handshake is signed using Bybit V5 authentication protocol before stream subscriptions are made.
- **SubscriptionManager**: Thread-safe storage that keeps track of active public and private topics and automatically resubscribes them on reconnection.
- **MessageHandler**: Handles incoming JSON message frames, filters ping/pong heartbeats, maps them to Application record events (`MarketTickerUpdateEvent`, `OrderUpdateEvent`, etc.), and dispatches them to active stream queues without locking.

### 3. Stream Channels & IAsyncEnumerable

To avoid blocking or complex callback mechanisms, stream data is channeled using thread-safe `System.Threading.Channels`.
Events are pushed onto unbounded/drop-oldest queues inside stream classes (`MarketStream`, `OrderStream`, `PositionStream`) and consumed asynchronously using `ReceiveEventsAsync(cancellationToken)` which returns an `IAsyncEnumerable<T>`, respecting cancellations gracefully.

## Connection Lifecycle & Reconnects

The client implements a well-defined lifecycle represented by the `ConnectionState` enum:

- `Disconnected`: Sockets are closed and inactive.
- `Connecting`: Socket handshakes are in progress.
- `Connected`: Both public and private sockets are open, authenticated, and resubscribed.
- `Reconnecting`: A connection drop was detected, backing off before retry.
- `Failed`: Critical failure or maximum reconnect attempts reached.

### Automatic Reconnect & Backoff

On any socket closure or receive failure:
1. Current socket loops are terminated and disposed.
2. A reconnection loop is started using an **exponential backoff with jitter** algorithm (max 60 seconds delay).
3. Sockets are rebuilt, authenticated, and previous subscriptions from `SubscriptionManager` are automatically pushed.
