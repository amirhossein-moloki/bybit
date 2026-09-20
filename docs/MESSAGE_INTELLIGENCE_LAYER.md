# Message Intelligence Layer Architecture

## Overview
The Message Intelligence Layer provides context-aware classification, target resolution, and execution safety validation for incoming Telegram messages without modifying existing signal storage, risk management, or exchange execution pipelines.

```text
Telegram Message
        ↓
Message Ingestion (TelegramClientService / DefaultTelegramMessageReceiver)
        ↓
Persist Full Message + Metadata (ReplyToMessageId, MediaInfo, EditInfo)
        ↓
Context Resolution (MessageContextBuilder)
        ↓
Deterministic Fast Path (DeterministicRuleEngine)
        │
    ┌───┴────┐
    │        │
 Clear     Ambiguous
    │        │
    ▼        ▼
 Execute    AI Interpretation (AIIntentInterpreter)
               ↓
        Structured Intent (StructuredIntent)
               ↓
        Target Resolution (TargetResolver)
               ↓
        Safety / Business Validation (IntentSafetyValidator)
               ↓
        Execute / Shadow Mode (IntentExecutionRouter)
```

## Core Components

1. **Telegram Metadata Capture**:
   - Captures `ReplyToMessageId`, `MediaInfo`, and `EditInfo` from WTelegram updates in `TelegramClientService`.
   - Persisted on `TelegramMessageDto` and domain `TelegramMessage` entity in EF Core table `TelegramMessages` (Migration: `20260821000000_AddTelegramMessageReplyAndMetadata.cs`).

2. **Domain Models & DTOs**:
   - Enums: `IntelligenceMessageType`, `TradingIntent`, `IntentScope`.
   - `StructuredIntent`: Standardized classification payload containing `MessageType`, `Intent`, `Scope`, `Targets`, `Confidence`, `RequiresExecution`, `Reason`, and `DetectionMethod` ("RULE" or "AI").
   - `MessageIntelligenceContext`: Enriches current message with `ReplyContext`, `ConversationContext` (recent channel history), `RelatedSignals`, `ActivePositions`, `PendingOrders`, and `ChannelContext`.

3. **Context Builder (`MessageContextBuilder`)**:
   - Resolves reply message correlation using `ChannelId + ReplyToMessageId`.
   - Fetches recent channel conversation history and active signals.
   - Populates active domain positions and pending orders.

4. **Deterministic Fast Path (`DeterministicRuleEngine`)**:
   - Converts Persian/Arabic digits and characters.
   - Classifies fast-path commands (`ریسک فری کنید`, `کنسل کنید`, `ببندید`, `اوردرها رو برگردونید`).
   - Differentiates commands from status reports (`ریسک فری شد`, `یورو ریسک فری شده`, `تارگت اول✅`) and commentary (`۴ تا اوردر داریم منتظریم`).
   - Returns `null` for ambiguous natural language messages to trigger AI Fallback.

5. **AI Fallback (`AIIntentInterpreter`)**:
   - Reuses `IAIProvider` (`OpenRouterAIProvider` or `MockAIProvider`) and `IPromptTemplateEngine`.
   - Renders prompt template `Templates/AIPrompts/prompt_intent_v1.txt`.
   - Strips codeblock markdown, parses JSON, clamps confidence bounds, and handles AI exceptions gracefully.

6. **Target Resolution (`TargetResolver`)**:
   - Resolves target symbols across explicit targets, reply context, single active positions, or single pending orders.
   - Disables execution and marks intent as ambiguous if multiple targets exist without explicit symbol resolution.

7. **Safety & Business Validation (`IntentSafetyValidator`)**:
   - Enforces configurable minimum confidence thresholds (`MessageIntelligenceOptions.MinimumConfidence`, default 0.85).
   - Validates that target positions or orders actually exist in domain state before allowing execution.

8. **Router, Idempotency & Shadow Mode (`IntentExecutionRouter`)**:
   - Idempotency key deduplication: `intent-{channelId}-{messageId}-{intent}-{targets}`.
   - Shadow Mode (`MessageIntelligenceOptions.ShadowMode = true`): Evaluates classification, context, target resolution, and safety validation without executing live exchange mutations.
   - Command Execution: Routes validated commands to `IBreakEvenManager` and `IPositionCloseManager`.
   - Signal Execution: Routes `NEW_SIGNAL` candidates to `ISignalStorageQueue`.
