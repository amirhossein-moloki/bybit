using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Models.Events;
using TradingBot.Domain.Enums;
using TradingBot.Exchange.Bybit.Streams;

namespace TradingBot.Exchange.Bybit.WebSocket;

public class MessageHandler
{
    private readonly IMarketStream _marketStream;
    private readonly IOrderStream _orderStream;
    private readonly IPositionStream _positionStream;
    private readonly ILogger<MessageHandler> _logger;

    public MessageHandler(
        IMarketStream marketStream,
        IOrderStream orderStream,
        IPositionStream positionStream,
        ILogger<MessageHandler> logger)
    {
        _marketStream = marketStream ?? throw new ArgumentNullException(nameof(marketStream));
        _orderStream = orderStream ?? throw new ArgumentNullException(nameof(orderStream));
        _positionStream = positionStream ?? throw new ArgumentNullException(nameof(positionStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (!root.TryGetProperty("topic", out var topicProp))
            {
                // Check if it's a subscription response
                if (root.TryGetProperty("op", out var opProp) && opProp.GetString() == "subscribe")
                {
                    _logger.LogInformation("WebSocket subscription response: {Success}, RetMsg: {RetMsg}",
                        root.TryGetProperty("success", out var s) && s.GetBoolean(),
                        root.TryGetProperty("ret_msg", out var rm) ? rm.GetString() : "");
                }
                return Task.CompletedTask;
            }

            var topic = topicProp.GetString();
            if (string.IsNullOrEmpty(topic)) return Task.CompletedTask;

            if (topic.StartsWith("tickers."))
            {
                HandleTickerMessage(root);
            }
            else if (topic == "order")
            {
                HandleOrderMessage(root);
            }
            else if (topic == "position")
            {
                HandlePositionMessage(root);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MessageHandler: Failed to process WebSocket message frame.");
        }

        return Task.CompletedTask;
    }

    private void HandleTickerMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataProp)) return;

        var symbol = dataProp.TryGetProperty("symbol", out var s) ? s.GetString() : string.Empty;
        if (string.IsNullOrEmpty(symbol)) return;

        decimal.TryParse(dataProp.TryGetProperty("lastPrice", out var lp) ? lp.GetString() : "0", out var price);
        decimal.TryParse(dataProp.TryGetProperty("bid1Price", out var bp) ? bp.GetString() : "0", out var bidPrice);
        decimal.TryParse(dataProp.TryGetProperty("ask1Price", out var ap) ? ap.GetString() : "0", out var askPrice);
        decimal.TryParse(dataProp.TryGetProperty("volume24h", out var v) ? v.GetString() : "0", out var volume);

        var timestamp = DateTime.UtcNow;
        if (root.TryGetProperty("ts", out var tsProp))
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsProp.GetInt64()).UtcDateTime;
        }

        var tickerEvent = new MarketTickerUpdateEvent(
            symbol,
            price,
            bidPrice,
            askPrice,
            volume,
            timestamp
        );

        if (_marketStream is BybitMarketStream bybitMarketStream)
        {
            bybitMarketStream.Push(tickerEvent);
        }
    }

    private void HandleOrderMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Array) return;

        foreach (var item in dataProp.EnumerateArray())
        {
            var clientOrderId = item.TryGetProperty("orderLinkId", out var cid) ? (cid.GetString() ?? string.Empty) : string.Empty;
            var exchangeOrderId = item.TryGetProperty("orderId", out var eid) ? (eid.GetString() ?? string.Empty) : string.Empty;
            var symbol = item.TryGetProperty("symbol", out var s) ? (s.GetString() ?? string.Empty) : string.Empty;
            var rawStatus = item.TryGetProperty("orderStatus", out var os) ? (os.GetString() ?? string.Empty) : string.Empty;
            var rejectReason = item.TryGetProperty("rejectReason", out var rr) ? rr.GetString() : null;

            if (string.IsNullOrEmpty(clientOrderId) || string.IsNullOrEmpty(symbol)) continue;

            decimal.TryParse(item.TryGetProperty("price", out var p) ? p.GetString() : "0", out var price);
            decimal.TryParse(item.TryGetProperty("qty", out var q) ? q.GetString() : "0", out var quantity);
            decimal.TryParse(item.TryGetProperty("cumExecQty", out var cq) ? cq.GetString() : "0", out var cumExecQty);

            var status = MapStatus(rawStatus);

            var timestamp = DateTime.UtcNow;
            if (item.TryGetProperty("updatedTime", out var utProp) && long.TryParse(utProp.GetString(), out var ut))
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ut).UtcDateTime;
            }

            var orderEvent = new OrderUpdateEvent(
                clientOrderId,
                exchangeOrderId,
                symbol,
                status,
                price,
                quantity,
                cumExecQty,
                rejectReason,
                timestamp
            );

            if (_orderStream is BybitOrderStream bybitOrderStream)
            {
                bybitOrderStream.Push(orderEvent);
            }
        }
    }

    private void HandlePositionMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Array) return;

        foreach (var item in dataProp.EnumerateArray())
        {
            var symbol = item.TryGetProperty("symbol", out var s) ? s.GetString() : string.Empty;
            if (string.IsNullOrEmpty(symbol)) continue;

            decimal.TryParse(item.TryGetProperty("size", out var sz) ? sz.GetString() : "0", out var size);
            decimal.TryParse(item.TryGetProperty("entryPrice", out var ep) ? ep.GetString() : "0", out var entryPrice);
            var side = item.TryGetProperty("side", out var sd) ? sd.GetString() : string.Empty;
            decimal.TryParse(item.TryGetProperty("leverage", out var lev) ? lev.GetString() : "1", out var leverage);

            var timestamp = DateTime.UtcNow;
            if (item.TryGetProperty("updatedTime", out var utProp) && long.TryParse(utProp.GetString(), out var ut))
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ut).UtcDateTime;
            }

            var positionEvent = new PositionUpdateEvent(
                symbol,
                size,
                entryPrice,
                side ?? string.Empty,
                leverage,
                timestamp
            );

            if (_positionStream is BybitPositionStream bybitPositionStream)
            {
                bybitPositionStream.Push(positionEvent);
            }
        }
    }

    private OrderStatus MapStatus(string bybitStatus)
    {
        return bybitStatus.ToUpperInvariant() switch
        {
            "NEW" => OrderStatus.Accepted,
            "PARTIALLYFILLED" => OrderStatus.PartiallyFilled,
            "FILLED" => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "REJECTED" => OrderStatus.Rejected,
            _ => OrderStatus.Created
        };
    }
}
