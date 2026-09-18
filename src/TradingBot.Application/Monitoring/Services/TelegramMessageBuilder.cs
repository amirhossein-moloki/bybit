using System;
using System.Text.Json;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class TelegramMessageBuilder : ITelegramMessageBuilder
{
    private readonly IEventSanitizer _sanitizer;

    public TelegramMessageBuilder(IEventSanitizer sanitizer)
    {
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
    }

    public string BuildMessage(MonitoringEvent @event)
    {
        var eventType = @event.EventType;
        var timestampStr = @event.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        string formattedMessage;

        // Try parsing payload as JSON
        JsonElement payloadJson = default;
        bool hasJsonPayload = false;
        if (!string.IsNullOrWhiteSpace(@event.Payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(@event.Payload);
                payloadJson = doc.RootElement.Clone();
                hasJsonPayload = true;
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        // Get value helper from JSON
        string GetJsonVal(string propName, string fallback = "")
        {
            if (hasJsonPayload && payloadJson.ValueKind == JsonValueKind.Object && payloadJson.TryGetProperty(propName, out var prop))
            {
                return prop.ToString();
            }
            return fallback;
        }

        switch (eventType)
        {
            case "ApplicationStarted":
                formattedMessage = $"🟢 <b>Trading Bot Started</b>\n\nStatus: Running\nTime: {timestampStr} UTC";
                break;

            case "ApplicationStopping":
            case "ApplicationStopped":
                formattedMessage = $"🔴 <b>Trading Bot Stopped</b>\n\nStatus: Stopped\nTime: {timestampStr} UTC";
                break;

            case "BybitDisconnected":
            case "Disconnected":
                formattedMessage = $"⚠️ <b>Connection Lost</b>\n\nService: {EscapeHtml(@event.Source)}\nStatus: Disconnected\nTime: {timestampStr} UTC\nCorrelationId: {EscapeHtml(@event.CorrelationId ?? "N/A")}";
                break;

            case "BybitConnectionRestored":
            case "Connected":
            case "ConnectionRestored":
                formattedMessage = $"🟢 <b>Connection Restored</b>\n\nService: {EscapeHtml(@event.Source)}\nStatus: Connected\nTime: {timestampStr} UTC";
                break;

            case "OrderFilled":
                {
                    var symbol = GetJsonVal("Symbol", GetJsonVal("symbol", ""));
                    var side = GetJsonVal("Side", GetJsonVal("side", "BUY"));
                    var qty = GetJsonVal("ExecutedQuantity", GetJsonVal("Quantity", GetJsonVal("quantity", "N/A")));
                    var price = GetJsonVal("ExecutedPrice", GetJsonVal("Price", GetJsonVal("price", "N/A")));
                    var orderId = @event.OrderId?.ToString() ?? GetJsonVal("OrderId", GetJsonVal("orderId", "N/A"));

                    if (string.IsNullOrEmpty(symbol))
                    {
                        symbol = ExtractField(@event.Message, "Symbol ", ".");
                    }

                    formattedMessage = $"📈 <b>Order Filled</b>\n\nSymbol: {EscapeHtml(symbol)}\nSide: {EscapeHtml(side)}\nQuantity: {EscapeHtml(qty)}\nPrice: {EscapeHtml(price)}\nOrder ID: {EscapeHtml(orderId)}";
                }
                break;

            case "OrderRejected":
                {
                    var symbol = GetJsonVal("Symbol", GetJsonVal("symbol", ""));
                    var side = GetJsonVal("Side", GetJsonVal("side", "BUY"));
                    var reason = GetJsonVal("Reason", GetJsonVal("reason", @event.Message));
                    var orderId = @event.OrderId?.ToString() ?? GetJsonVal("OrderId", GetJsonVal("orderId", "N/A"));

                    if (string.IsNullOrEmpty(symbol))
                    {
                        symbol = ExtractField(@event.Message, "Symbol ", ".");
                    }

                    formattedMessage = $"❌ <b>Order Rejected</b>\n\nSymbol: {EscapeHtml(symbol)}\nSide: {EscapeHtml(side)}\nReason: {EscapeHtml(reason)}\nOrder ID: {EscapeHtml(orderId)}";
                }
                break;

            case "PositionOpened":
                {
                    var symbol = GetJsonVal("Symbol", GetJsonVal("symbol", ""));
                    var side = GetJsonVal("Side", GetJsonVal("side", "LONG"));
                    var qty = GetJsonVal("Quantity", GetJsonVal("quantity", "N/A"));
                    var entry = GetJsonVal("EntryPrice", GetJsonVal("entryPrice", "N/A"));
                    var positionId = @event.PositionId?.ToString() ?? GetJsonVal("PositionId", GetJsonVal("positionId", "N/A"));

                    if (string.IsNullOrEmpty(symbol))
                    {
                        symbol = ExtractField(@event.Message, "Symbol ", ".");
                    }

                    formattedMessage = $"🟢 <b>Position Opened</b>\n\nSymbol: {EscapeHtml(symbol)}\nSide: {EscapeHtml(side)}\nQuantity: {EscapeHtml(qty)}\nEntry: {EscapeHtml(entry)}\nPosition ID: {EscapeHtml(positionId)}";
                }
                break;

            case "PositionClosed":
                {
                    var symbol = GetJsonVal("Symbol", GetJsonVal("symbol", ""));
                    var side = GetJsonVal("Side", GetJsonVal("side", "LONG"));
                    var exit = GetJsonVal("ExitPrice", GetJsonVal("exitPrice", "N/A"));
                    var pnl = GetJsonVal("RealizedPnL", GetJsonVal("realizedPnL", "N/A"));
                    var reason = GetJsonVal("Reason", GetJsonVal("reason", "N/A"));

                    if (string.IsNullOrEmpty(symbol))
                    {
                        symbol = ExtractField(@event.Message, "Symbol ", ".");
                    }

                    formattedMessage = $"🔴 <b>Position Closed</b>\n\nSymbol: {EscapeHtml(symbol)}\nSide: {EscapeHtml(side)}\nExit Price: {EscapeHtml(exit)}\nRealized PnL: {EscapeHtml(pnl)}\nReason: {EscapeHtml(reason)}";
                }
                break;

            case "TelegramMessageListened":
            case "SignalRejected":
            case "DuplicateSignal":
            case "RiskRejected":
            case "TradeExecutedFromSignal":
            case "TradeExecutionFailed":
                {
                    var status = GetJsonVal("Status", @event.Status ?? "INFO");
                    var reason = GetJsonVal("Reason", @event.Message);
                    var sourceTitle = GetJsonVal("SourceTitle", @event.Source);
                    var chatId = GetJsonVal("ChatId", "N/A");
                    var messageId = GetJsonVal("MessageId", "N/A");
                    var senderId = GetJsonVal("SenderId", "N/A");
                    var rawText = GetJsonVal("RawText", GetJsonVal("MessageText", ""));
                    var symbol = GetJsonVal("Symbol", "");
                    var side = GetJsonVal("Side", "");
                    var entry = GetJsonVal("EntryPrice", "");
                    var sl = GetJsonVal("StopLoss", "");
                    var tp = GetJsonVal("TakeProfit", "");
                    var leverage = GetJsonVal("Leverage", "");
                    var orderId = GetJsonVal("OrderId", "");

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"📩 <b>[Telegram Intercepted Message]</b>");
                    sb.AppendLine($"<b>Source:</b> {EscapeHtml(sourceTitle)} (<code>{EscapeHtml(chatId)}</code>)");
                    sb.AppendLine($"<b>Message ID:</b> <code>{EscapeHtml(messageId)}</code>");
                    if (!string.IsNullOrEmpty(senderId) && senderId != "N/A")
                        sb.AppendLine($"<b>Sender ID:</b> <code>{EscapeHtml(senderId)}</code>");
                    sb.AppendLine($"<b>Time:</b> {timestampStr} UTC");

                    if (!string.IsNullOrEmpty(symbol))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"<b>Extracted Signal Parameters:</b>");
                        sb.AppendLine($"• <b>Symbol:</b> <code>{EscapeHtml(symbol)}</code>");
                        if (!string.IsNullOrEmpty(side)) sb.AppendLine($"• <b>Side:</b> {EscapeHtml(side)}");
                        if (!string.IsNullOrEmpty(entry)) sb.AppendLine($"• <b>Entry Price:</b> {EscapeHtml(entry)}");
                        if (!string.IsNullOrEmpty(sl)) sb.AppendLine($"• <b>Stop Loss:</b> {EscapeHtml(sl)}");
                        if (!string.IsNullOrEmpty(tp)) sb.AppendLine($"• <b>Take Profit:</b> {EscapeHtml(tp)}");
                        if (!string.IsNullOrEmpty(leverage)) sb.AppendLine($"• <b>Leverage:</b> {EscapeHtml(leverage)}x");
                        if (!string.IsNullOrEmpty(orderId)) sb.AppendLine($"• <b>Order ID:</b> <code>{EscapeHtml(orderId)}</code>");
                    }

                    sb.AppendLine();
                    sb.AppendLine($"<b>Status:</b> {EscapeHtml(status)}");
                    sb.AppendLine($"<b>Reason:</b> {EscapeHtml(reason)}");

                    if (!string.IsNullOrEmpty(rawText))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"<b>Raw Content:</b>");
                        sb.AppendLine($"<i>{EscapeHtml(rawText)}</i>");
                    }

                    formattedMessage = sb.ToString().TrimEnd();
                }
                break;

            case "ApplicationError":
            case "Error":
            case "WorkerFailed":
                formattedMessage = $"❌ <b>System Error</b>\n\nComponent: {EscapeHtml(@event.Component)}\nOperation: {EscapeHtml(@event.Status)}\nError: {EscapeHtml(@event.Message)}\nCorrelationId: {EscapeHtml(@event.CorrelationId ?? "N/A")}";
                break;

            case "CriticalError":
            case "Critical":
                formattedMessage = $"🚨 <b>CRITICAL SYSTEM ERROR</b>\n\nComponent: {EscapeHtml(@event.Component)}\nOperation: {EscapeHtml(@event.Status)}\nError: {EscapeHtml(@event.Message)}\nCorrelationId: {EscapeHtml(@event.CorrelationId ?? "N/A")}";
                break;

            default:
                var severityEmoji = @event.Severity == "CRITICAL" ? "🚨" : (@event.Severity == "ERROR" ? "❌" : (@event.Severity == "WARNING" ? "⚠️" : "ℹ️"));
                formattedMessage = $"{severityEmoji} <b>{EscapeHtml(@event.EventType)}</b>\n\nSource: {EscapeHtml(@event.Source)}\nComponent: {EscapeHtml(@event.Component)}\nMessage: {EscapeHtml(@event.Message)}\nTime: {timestampStr} UTC";
                break;
        }

        // Apply secret protection and limit message length
        var sanitizedMessage = _sanitizer.Sanitize(formattedMessage) ?? string.Empty;
        if (sanitizedMessage.Length > 4000)
        {
            sanitizedMessage = sanitizedMessage[..3980] + "... [TRUNCATED]";
        }

        return sanitizedMessage;
    }

    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string ExtractField(string message, string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var idx = message.IndexOf(prefix);
        if (idx == -1) return string.Empty;
        var start = idx + prefix.Length;
        var end = message.IndexOf(suffix, start);
        if (end == -1) return message[start..].Trim();
        return message[start..end].Trim();
    }
}
