# Message Receiver Architecture

This document describes the message receiving architecture and the pipeline used to process, map, and dispatch incoming Telegram channel/group messages to down-stream layers.

---

## 1. Architectural Overview

The message receiving pipeline strictly adheres to Clean Architecture guidelines. The Telegram client integration module handles communication with MTProto, isolates raw Telegram objects, and maps events to application-ready DTOs before dispatching them.

```
       MTProto Event
             │
             ▼
   TelegramClientService  <── (Initializes UpdateManager / caches dialogs)
             │
      Update Handler (OnUpdateCallback)
             │
             ▼
     TelegramMessageDto   (Application DTO)
             │
             ▼
   ITelegramMessageReceiver (Dispatches to Default or Custom receivers)
             │
             ▼
         Next Stage       (Signal Parsing / Strategy Engine)
```

No database persistence, signal parsing, or trading logic is introduced in this stage, maintaining a strict separation of concerns.

---

## 2. Dynamic Monitored Channel Management

Channel monitoring utilizes flexible configurations:

```json
{
  "Telegram": {
    "Channels": [
      "CryptoSignalsVIP",
      "BTCSignals",
      "123456789"
    ]
  }
}
```

- **Dynamic Loading**: Configuration updates are supported through dependency-injected configurations.
- **Ignore Unknown Chats**: Any message whose Peer/Chat ID, Title, or Username does not match the configured list of Channels is silently dropped. This prevents processing spam or messages from unauthorized chats.

---

## 3. Resilience and Connection State

The message receiving pipeline relies on `TelegramListenerWorker` to keep the connection healthy:

- **State Propagation**: Transitioning states are lock-guarded and thread-safe.
- **Automatic Recovery**: Backoff retry handling recovers connection gaps during network brownouts or MTProto temporary failures.
- **Health Checks**: `TelegramHealthCheck` reports `Healthy`, `Degraded` (during Connecting/Authenticating/Reconnecting), or `Unhealthy` (during Disconnected/Error) states.

---

## 4. Message Event Pipeline Trace

1. **Incoming Message**: WTelegramClient receives a new event.
2. **Sequential Queueing**: `WithUpdateManager` ensures sequence ordering and gap-filling.
3. **Filtering**: Non-message updates, media-only updates, direct messages, and edited/deleted items are discarded.
4. **Information Resolution**: Dialog titles/usernames are resolved against cached dialog maps.
5. **DTO Formulation**: Original IDs and UTC timestamps are captured into a `TelegramMessageDto`.
6. **Receiver Dispatch**: The message DTO is handed over to the registered `ITelegramMessageReceiver` for processing.
