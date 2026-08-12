using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Domain.Entities;

namespace TradingBot.Exchange.Bybit.WebSocket;

public class BybitWebSocketClient : IExchangeStreamClient
{
    private readonly BybitSettings _settings;
    private readonly ILogger<BybitWebSocketClient> _logger;
    private readonly SubscriptionManager _subscriptionManager;
    private readonly MessageHandler _messageHandler;
    private readonly IResilienceService _resilienceService;
    private readonly IServiceProvider _serviceProvider;

    private ClientWebSocket? _publicSocket;
    private ClientWebSocket? _privateSocket;
    private CancellationTokenSource? _connectionCts;

    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _reconnectSemaphore = new(1, 1);

    public bool IsRecoveryIncomplete { get; private set; }

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
        IServiceProvider serviceProvider,
        ILogger<BybitWebSocketClient> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        MarketStream = marketStream ?? throw new ArgumentNullException(nameof(marketStream));
        OrderStream = orderStream ?? throw new ArgumentNullException(nameof(orderStream));
        PositionStream = positionStream ?? throw new ArgumentNullException(nameof(positionStream));
        _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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

            // Reset incomplete recovery flag on successful fresh connection
            IsRecoveryIncomplete = false;

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

        var publicUrl = _settings.UseSandbox
            ? "wss://stream-testnet.bybit.com/v5/public/spot"
            : "wss://stream.bybit.com/v5/public/spot";

        var privateUrl = _settings.UseSandbox
            ? "wss://stream-testnet.bybit.com/v5/private"
            : "wss://stream.bybit.com/v5/private";

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
        if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.ApiSecret))
        {
            _logger.LogWarning("WebSocket: API Key or Secret is not configured. Private stream will not be authenticated.");
            return;
        }

        var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
        var rawSig = $"GET/realtime{expires}";

        string signature;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.ApiSecret)))
        {
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSig));
            signature = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var authPayload = new
        {
            op = "auth",
            args = new object[] { _settings.ApiKey, expires, signature }
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

    private async Task HandleReconnectAsync()
    {
        if (!await _reconnectSemaphore.WaitAsync(0))
        {
            _logger.LogInformation("WebSocket: Reconnection already in progress. Skipping duplicate request.");
            return;
        }

        try
        {
            lock (_stateLock)
            {
                if (State == ConnectionState.Reconnecting || State == ConnectionState.Disconnected)
                {
                    return;
                }
                State = ConnectionState.Reconnecting;
            }

            IsRecoveryIncomplete = false; // Reset status on starting reconnection

            _logger.LogWarning("WebSocket: Disconnection detected. Initiating automatic reconnect...");
            await PublishEventAsync("BybitConnectionLost", "WARNING", "Reconnecting", "Bybit WebSocket connection lost. Reconnecting...");

            _connectionCts?.Cancel();
            _connectionCts = new CancellationTokenSource();

            var attempt = 0;
            var maxAttempts = 10;

            while (attempt < maxAttempts)
            {
                attempt++;
                await PublishEventAsync("BybitReconnectStarted", "WARNING", "Reconnecting", $"Bybit WebSocket reconnect starting (Attempt {attempt})...");

                // Use IRetryDelayCalculator to compute backoff delay
                var delayCalculator = _serviceProvider.GetService<IRetryDelayCalculator>();
                var reliabilityOptions = _serviceProvider.GetService<ReliabilityOptions>() ?? new ReliabilityOptions();
                var delay = delayCalculator?.CalculateDelay(attempt, reliabilityOptions) ?? TimeSpan.FromSeconds(5);

                _logger.LogInformation("WebSocket: Reconnection attempt #{Attempts} in {Delay}ms...", attempt, delay.TotalMilliseconds);

                try
                {
                    await Task.Delay(delay, _connectionCts.Token);

                    // Direct connection execution
                    State = ConnectionState.Connecting;
                    await ConnectSocketsAsync(_connectionCts.Token);
                    State = ConnectionState.Connected;

                    _logger.LogInformation("WebSocket: Connected to Bybit successfully after reconnect.");
                    await PublishEventAsync("BybitReconnectSucceeded", "INFORMATION", "Connected", "Bybit WebSocket reconnected successfully.");

                    // Start receive loops for the new sockets
                    _ = ReceiveLoopAsync(_publicSocket, "Public", _connectionCts.Token);
                    _ = ReceiveLoopAsync(_privateSocket, "Private", _connectionCts.Token);

                    // Re-subscribe to any previous subscriptions cleanly without duplicates
                    await ResubscribeAllAsync(_connectionCts.Token);

                    // Perform post-reconnect synchronization
                    await ResynchronizeAfterReconnectAsync(_connectionCts.Token);
                    break;
                }
                catch (OperationCanceledException) when (_connectionCts.IsCancellationRequested)
                {
                    _logger.LogInformation("WebSocket: Reconnection cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket: Reconnection attempt #{Attempts} failed.", attempt);
                    await PublishEventAsync("BybitReconnectFailed", "ERROR", "Reconnecting", $"Bybit WebSocket reconnect attempt {attempt} failed: {ex.Message}");

                    if (attempt >= maxAttempts)
                    {
                        _logger.LogCritical("WebSocket: Maximum reconnection attempts reached. Failing connection permanently.");
                        State = ConnectionState.Failed;
                        break;
                    }
                }
            }
        }
        finally
        {
            _reconnectSemaphore.Release();
        }
    }

    private async Task ResynchronizeAfterReconnectAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("WebSocket: Starting post-reconnect resynchronization...");
        await PublishEventAsync("BybitResynchronizationStarted", "INFORMATION", "Resynchronizing", "Post-reconnect synchronization started.");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var positionSync = scope.ServiceProvider.GetService<IPositionSynchronizationService>();
            var orderReconciliation = scope.ServiceProvider.GetService<IOrderReconciliationService>();

            if (positionSync != null)
            {
                await positionSync.SynchronizeAsync(cancellationToken);
            }
            if (orderReconciliation != null)
            {
                await orderReconciliation.ReconcileAsync(cancellationToken);
            }

            IsRecoveryIncomplete = false;
            _logger.LogInformation("WebSocket: Post-reconnect resynchronization completed successfully.");
            await PublishEventAsync("BybitResynchronizationCompleted", "INFORMATION", "Healthy", "Post-reconnect synchronization completed successfully.");
        }
        catch (Exception ex)
        {
            IsRecoveryIncomplete = true;
            _logger.LogError(ex, "WebSocket: Post-reconnect resynchronization failed.");
            await PublishEventAsync("BybitResynchronizationFailed", "ERROR", "Degraded", $"Post-reconnect synchronization failed: {ex.Message}");
        }
    }

    private async Task PublishEventAsync(string eventType, string severity, string status, string message)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var publisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (publisher != null)
            {
                var @event = new MonitoringEvent(
                    eventType: eventType,
                    severity: severity,
                    source: "Exchange",
                    component: "BybitWebSocket",
                    status: status,
                    message: message
                );
                await publisher.PublishAsync(@event, forceSynchronous: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish WebSocket monitoring event.");
        }
    }
}
