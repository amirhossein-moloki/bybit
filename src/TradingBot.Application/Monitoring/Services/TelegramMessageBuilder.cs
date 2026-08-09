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
