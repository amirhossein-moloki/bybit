using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;

namespace TradingBot.Exchange.Bybit.WebSocket;

public class BybitWebSocketClient : IExchangeStreamClient
{
    private readonly BybitSettings _settings;
    private readonly ILogger<BybitWebSocketClient> _logger;
    private readonly SubscriptionManager _subscriptionManager;
    private readonly MessageHandler _messageHandler;
    private readonly IResilienceService _resilienceService;

    private ClientWebSocket? _publicSocket;
    private ClientWebSocket? _privateSocket;
    private CancellationTokenSource? _connectionCts;

    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly object _stateLock = new();

    public ConnectionState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
        private set
        {
            lock (_stateLock)
            {
                if (_state != value)
                {
                    _state = value;
                    StateChanged?.Invoke(_state);
                }
            }
        }
    }

    public event Action<ConnectionState>? StateChanged;

    public IMarketStream MarketStream { get; }
    public IOrderStream OrderStream { get; }
    public IPositionStream PositionStream { get; }

    public BybitWebSocketClient(
        BybitSettings settings,
        IMarketStream marketStream,
        IOrderStream orderStream,
        IPositionStream positionStream,
        SubscriptionManager subscriptionManager,
        MessageHandler messageHandler,
        IResilienceService resilienceService,
        ILogger<BybitWebSocketClient> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        MarketStream = marketStream ?? throw new ArgumentNullException(nameof(marketStream));
        OrderStream = orderStream ?? throw new ArgumentNullException(nameof(orderStream));
        PositionStream = positionStream ?? throw new ArgumentNullException(nameof(positionStream));
        _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state == ConnectionState.Connecting || _state == ConnectionState.Connected)
            {
                _logger.LogInformation("WebSocket: ConnectAsync called but state is already {State}. Ignoring.", _state);
                return;
            }
        }

        State = ConnectionState.Connecting;
        _logger.LogInformation("WebSocket: Connecting to Bybit WebSocket stream...");

        _connectionCts = new CancellationTokenSource();

        try
        {
            await _resilienceService.ExecuteWebSocketAsync(async ct =>
            {
                await ConnectSocketsAsync(ct);
            }, cancellationToken);
            State = ConnectionState.Connected;
            _logger.LogInformation("WebSocket: Connected to Bybit successfully.");

            // Start processing incoming messages in the background
            _ = ReceiveLoopAsync(_publicSocket, "Public", _connectionCts.Token);
            _ = ReceiveLoopAsync(_privateSocket, "Private", _connectionCts.Token);

            // Re-subscribe to any previous subscriptions
            await ResubscribeAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket: Failed to connect to Bybit WebSocket.");
            State = ConnectionState.Failed;
            _ = HandleReconnectAsync();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WebSocket: Disconnecting from Bybit WebSocket stream...");
        _connectionCts?.Cancel();

        State = ConnectionState.Disconnected;

        if (_publicSocket != null)
        {
            try
            {
                if (_publicSocket.State == WebSocketState.Open)
                {
                    await _publicSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect request", cancellationToken);
                }
                _publicSocket.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket: Exception during public socket closure.");
            }
        }

        if (_privateSocket != null)
        {
            try
            {
                if (_privateSocket.State == WebSocketState.Open)
                {
                    await _privateSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect request", cancellationToken);
                }
                _privateSocket.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket: Exception during private socket closure.");
            }
        }

        _logger.LogInformation("WebSocket: Disconnected.");
    }

    public async Task SubscribePublicAsync(string topic, CancellationToken cancellationToken = default)
    {
        _subscriptionManager.AddPublicSubscription(topic);

        if (State == ConnectionState.Connected && _publicSocket != null && _publicSocket.State == WebSocketState.Open)
        {
            await SendSubscriptionRequestAsync(_publicSocket, new[] { topic }, cancellationToken);
        }
    }

    public async Task SubscribePrivateAsync(string topic, CancellationToken cancellationToken = default)
    {
        _subscriptionManager.AddPrivateSubscription(topic);

        if (State == ConnectionState.Connected && _privateSocket != null && _privateSocket.State == WebSocketState.Open)
        {
            await SendSubscriptionRequestAsync(_privateSocket, new[] { topic }, cancellationToken);
        }
    }

    private async Task ConnectSocketsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _publicSocket?.Dispose();
        }
        catch { }

        try
        {
            _privateSocket?.Dispose();
        }
        catch { }

        var publicUrl = BybitOptions.GetPublicWebSocketUrl(_settings.Environment, "spot");
        var privateUrl = BybitOptions.GetPrivateWebSocketUrl(_settings.Environment);

        _publicSocket = new ClientWebSocket();
        _privateSocket = new ClientWebSocket();

        _logger.LogInformation("WebSocket: Connecting to public stream: {Url}", publicUrl);
        await _publicSocket.ConnectAsync(new Uri(publicUrl), cancellationToken);

        _logger.LogInformation("WebSocket: Connecting to private stream: {Url}", privateUrl);
        await _privateSocket.ConnectAsync(new Uri(privateUrl), cancellationToken);

        // Authenticate private stream
        await AuthenticatePrivateSocketAsync(cancellationToken);
    }

    private async Task AuthenticatePrivateSocketAsync(CancellationToken cancellationToken)
    {
        var apiKey = _settings.EffectiveApiKey;
        var apiSecret = _settings.EffectiveApiSecret;

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogWarning("WebSocket: API Key or Secret is not configured. Private stream will not be authenticated.");
            return;
        }

        var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
        var rawSig = $"GET/realtime{expires}";

        string signature;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
        {
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSig));
            signature = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var authPayload = new
        {
            op = "auth",
            args = new object[] { apiKey, expires, signature }
        };

        var json = JsonSerializer.Serialize(authPayload);
        var bytes = Encoding.UTF8.GetBytes(json);

        _logger.LogInformation("WebSocket: Authenticating private WebSocket connection...");
        await _privateSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task SendSubscriptionRequestAsync(ClientWebSocket socket, string[] topics, CancellationToken cancellationToken)
    {
        var subPayload = new
        {
            op = "subscribe",
            args = topics
        };

        var json = JsonSerializer.Serialize(subPayload);
        var bytes = Encoding.UTF8.GetBytes(json);

        _logger.LogInformation("WebSocket: Subscribing to topics: {Topics}", string.Join(", ", topics));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ResubscribeAllAsync(CancellationToken cancellationToken)
    {
        var publicSubs = _subscriptionManager.GetPublicSubscriptions().ToArray();
        if (publicSubs.Length > 0 && _publicSocket != null && _publicSocket.State == WebSocketState.Open)
        {
            await SendSubscriptionRequestAsync(_publicSocket, publicSubs, cancellationToken);
        }

        var privateSubs = _subscriptionManager.GetPrivateSubscriptions().ToArray();
        if (privateSubs.Length > 0 && _privateSocket != null && _privateSocket.State == WebSocketState.Open)
        {
            await SendSubscriptionRequestAsync(_privateSocket, privateSubs, cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket? socket, string socketName, CancellationToken cancellationToken)
    {
        if (socket == null) return;

        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("WebSocket: {SocketName} socket closed by exchange.", socketName);
                        break;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (ms.Length == 0) continue;

                var message = Encoding.UTF8.GetString(ms.ToArray());

                // Heartbeat / ping responses logic
                if (message.Contains("\"op\":\"ping\"") || message.Contains("\"ret_msg\":\"pong\"") || message.Contains("pong"))
                {
                    _logger.LogDebug("WebSocket: Received heartbeat/ping response from {SocketName}.", socketName);
                    continue;
                }

                _logger.LogDebug("WebSocket: {SocketName} received message: {Message}", socketName, message);

                try
                {
                    await _messageHandler.HandleMessageAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket: Error handling message from {SocketName} socket.", socketName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket: Receive loop cancelled for {SocketName} socket.", socketName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket: Exception in receive loop for {SocketName} socket.", socketName);
            _ = HandleReconnectAsync();
        }
    }

    private int _reconnectAttempts = 0;

    private async Task HandleReconnectAsync()
    {
        lock (_stateLock)
        {
            if (State == ConnectionState.Reconnecting || State == ConnectionState.Disconnected)
            {
                return;
            }
            State = ConnectionState.Reconnecting;
        }

        _logger.LogWarning("WebSocket: Disconnection detected. Initiating automatic reconnect...");

        _connectionCts?.Cancel();

        while (true)
        {
            _reconnectAttempts++;
            var delayMs = Math.Min(1000 * (int)Math.Pow(2, _reconnectAttempts), 60000); // Exponential backoff max 60s
            _logger.LogInformation("WebSocket: Reconnection attempt #{Attempts} in {Delay}ms...", _reconnectAttempts, delayMs);

            try
            {
                await Task.Delay(delayMs, CancellationToken.None);
                await ConnectAsync(CancellationToken.None);
                _reconnectAttempts = 0;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket: Reconnection attempt #{Attempts} failed.", _reconnectAttempts);
                if (_reconnectAttempts >= 10)
                {
                    _logger.LogCritical("WebSocket: Maximum reconnection attempts reached. Failing connection permanently.");
                    State = ConnectionState.Failed;
                    break;
                }
            }
        }
    }
}
