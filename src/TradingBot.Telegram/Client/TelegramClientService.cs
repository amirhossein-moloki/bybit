using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using WTelegram;
using TL;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Client;

public class TelegramClientService : ITelegramClient, ITelegramDiscoveryClient, IDisposable
{
    private readonly TelegramOptions _options;
    private readonly ITelegramSessionManager _sessionManager;
    private readonly ITelegramMessageReceiver _messageReceiver;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger _logger;
    private WTelegram.Client? _client;
    private WTelegram.UpdateManager? _updateManager;
    private TelegramConnectionState _currentState = TelegramConnectionState.Disconnected;
    private readonly object _stateLock = new();
    private readonly System.Collections.Generic.HashSet<string> _dynamicMonitoredChannels = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public Func<string>? VerificationCodeProvider { get; set; }
    public Func<string>? PasswordProvider { get; set; }

    public TelegramClientService(
        IOptions<TelegramOptions> options,
        ITelegramSessionManager sessionManager,
        ITelegramMessageReceiver messageReceiver,
        IServiceScopeFactory? scopeFactory = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _scopeFactory = scopeFactory;
        _logger = Log.ForContext<TelegramClientService>();
    }

    public TelegramConnectionState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    public void SetState(TelegramConnectionState state)
    {
        lock (_stateLock)
        {
            var oldState = _currentState;
            if (oldState != state)
            {
                _currentState = state;
                _logger.Information("Telegram connection state changed from {OldState} to {NewState}", oldState, state);
            }
        }
    }

    public async Task ConnectAsync()
    {
        if (!_options.Enabled)
        {
            _logger.Warning("Telegram integration is disabled in configuration.");
            return;
        }

        if (IsConnected())
        {
            _logger.Information("Telegram client is already connected.");
            return;
        }

        try
        {
            SetState(TelegramConnectionState.Connecting);
            _logger.Information("Telegram connection started");

            if (_client == null)
            {
                var sessionStream = _sessionManager.LoadSession();
                _client = new WTelegram.Client(ConfigProvider, sessionStream);
                ConfigureProxyIfNeeded(_client);
            }

            // Connect to Telegram
            await _client.ConnectAsync();

            SetState(TelegramConnectionState.Connected);
            _logger.Information("Telegram Connected");
        }
        catch (Exception ex)
        {
            SetState(TelegramConnectionState.Error);
            _logger.Error(ex, "Failed to connect to Telegram.");
            throw new TelegramConnectionException("Failed to establish connection to Telegram.", ex);
        }
    }

    public async Task<TL.User?> LoginWithQrCodeAsync(Action<string> qrDisplay, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Telegram integration is disabled in configuration.");
        }

        if (_client == null)
        {
            var sessionStream = _sessionManager.LoadSession();
            _client = new WTelegram.Client(ConfigProvider, sessionStream);
            ConfigureProxyIfNeeded(_client);
        }

        if (_client.Disconnected)
        {
            await _client.ConnectAsync();
        }

        return await _client.LoginWithQRCode(qrDisplay, ct: ct);
    }

    public TelegramAccountDto? GetConnectedAccount()
    {
        if (_client?.User == null) return null;

        var user = _client.User;
        return new TelegramAccountDto
        {
            Id = user.id,
            Username = user.username,
            FirstName = user.first_name,
            LastName = user.last_name,
            Phone = user.phone
        };
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
            _updateManager = null;
            SetState(TelegramConnectionState.Disconnected);
            _logger.Information("Disconnected");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SetState(TelegramConnectionState.Error);
            _logger.Error(ex, "Failed to disconnect Telegram client.");
            throw new TelegramConnectionException("Error occurred during disconnection.", ex);
        }
    }

    public bool IsConnected()
    {
        lock (_stateLock)
        {
            return (_currentState == TelegramConnectionState.Connected || _currentState == TelegramConnectionState.Listening) && _client != null;
        }
    }

    public async Task InitializeListeningAsync()
    {
        if (_client == null)
        {
            throw new TelegramConnectionException("WTelegram client is not initialized.");
        }

        _logger.Information("Subscribing to Telegram updates using UpdateManager...");

        // Use WithUpdateManager to subscribe to update events
        _updateManager = _client.WithUpdateManager(OnUpdateCallback);

        // Fetch dialogs to populate UpdateManager.Users and UpdateManager.Chats cache
        _logger.Information("Fetching Telegram dialogs to populate update manager cache...");
        var dialogs = await _client.Messages_GetAllDialogs();
        dialogs.CollectUsersChats(_updateManager.Users, _updateManager.Chats);
        _logger.Information("Loaded and cached {ChatCount} chats from Telegram dialogs.", _updateManager.Chats.Count);
    }

    public async Task SendMessageAsync(long chatId, string message)
    {
        if (_client == null)
        {
            throw new TelegramConnectionException("Telegram client is not initialized.");
        }

        TL.ChatBase? chat = null;
        if (_updateManager != null && _updateManager.Chats.TryGetValue(chatId, out var cachedChat))
        {
            chat = cachedChat;
        }

        if (chat == null)
        {
            var dialogs = await _client.Messages_GetAllDialogs();
            if (_updateManager != null)
            {
                dialogs.CollectUsersChats(_updateManager.Users, _updateManager.Chats);
                if (_updateManager.Chats.TryGetValue(chatId, out cachedChat))
                {
                    chat = cachedChat;
                }
            }
            else
            {
                if (dialogs.chats.TryGetValue(chatId, out var dialogChat))
                {
                    chat = dialogChat;
                }
            }
        }

        if (chat == null)
        {
            throw new TelegramConnectionException($"Chat with ID {chatId} not found in Telegram dialogs/chats cache.");
        }

        var entities = _client.HtmlToEntities(ref message);
        await _client.SendMessageAsync(chat, message, entities: entities);
    }

    public WTelegram.Client? UnderlyingClient => _client;

    private async Task OnUpdateCallback(TL.Update update)
    {
        try
        {
            switch (update)
            {
                case TL.UpdateNewChannelMessage uncm:
                    await HandleMessageBaseAsync(uncm.message, update);
                    break;
                case TL.UpdateNewMessage unm:
                    await HandleMessageBaseAsync(unm.message, update);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Telegram update of type {UpdateType}", update.GetType().Name);
        }
    }

    private async Task HandleMessageBaseAsync(TL.MessageBase messageBase, TL.Update rawUpdate)
    {
        if (messageBase is not TL.Message m)
        {
            return; // Ignore MessageEmpty or MessageService
        }

        // Ignore edited or deleted messages (this is only for new messages)
        // Ignore media-only events (i.e. message text is empty)
        if (string.IsNullOrWhiteSpace(m.message))
        {
            _logger.Debug("Ignoring empty or media-only message ID {MessageId}", m.id);
            return;
        }

        if (_updateManager == null)
        {
            _logger.Warning("Update manager is not initialized. Cannot resolve chat info.");
            return;
        }

        var peerInfo = _updateManager.UserOrChat(m.peer_id);
        if (peerInfo == null)
        {
            _logger.Warning("Could not resolve peer info for Peer {PeerId}", m.peer_id?.ID);
            return;
        }

        if (peerInfo is not TL.ChatBase chat)
        {
            // Ignore direct messages (User peer) since we only monitor channels and groups
            return;
        }

        // Check if the chat is monitored (Subscription Filter)
        if (!IsChannelMonitored(chat))
        {
            // Ignore unknown/unmonitored chats
            return;
        }

        bool isChannel = false;
        bool isGroup = false;
        string channelName = chat.Title ?? string.Empty;

        if (chat is TL.Channel tlChannel)
        {
            isChannel = tlChannel.IsChannel;
            isGroup = tlChannel.IsGroup;
            if (!string.IsNullOrEmpty(tlChannel.username))
            {
                channelName = tlChannel.username;
            }
        }
        else if (chat is TL.Chat tlChat)
        {
            isChannel = false;
            isGroup = true;
        }

        var dto = new TelegramMessageDto
        {
            ChannelId = chat.ID,
            ChannelName = channelName,
            MessageId = m.id,
            SenderId = m.from_id?.ID ?? 0,
            Text = m.message,
            Date = m.date.ToUniversalTime(),
            IsChannel = isChannel,
            IsGroup = isGroup,
            RawUpdate = rawUpdate?.GetType().Name ?? "UpdateNewMessage"
        };

        _logger.Information("Message Received: ID {MessageId} from channel {ChannelName} (ID: {ChannelId})", dto.MessageId, dto.ChannelName, dto.ChannelId);

        // Pass to message receiver
        await _messageReceiver.ReceiveMessageAsync(dto);
    }

    public System.Collections.Generic.List<string> GetMonitoredChannels()
    {
        lock (_stateLock)
        {
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_options.Channels != null)
            {
                foreach (var ch in _options.Channels)
                {
                    if (!string.IsNullOrWhiteSpace(ch)) set.Add(ch.Trim());
                }
            }
            foreach (var ch in _dynamicMonitoredChannels)
            {
                if (!string.IsNullOrWhiteSpace(ch)) set.Add(ch.Trim());
            }
            return new System.Collections.Generic.List<string>(set);
        }
    }

    public bool ToggleMonitoredChannel(string identifier, bool enable)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;
        identifier = identifier.Trim();

        lock (_stateLock)
        {
            _options.Channels ??= new System.Collections.Generic.List<string>();

            if (enable)
            {
                _dynamicMonitoredChannels.Add(identifier);
                if (!_options.Channels.Contains(identifier, StringComparer.OrdinalIgnoreCase))
                {
                    _options.Channels.Add(identifier);
                }
            }
            else
            {
                _dynamicMonitoredChannels.Remove(identifier);
                _options.Channels.RemoveAll(x => string.Equals(x, identifier, StringComparison.OrdinalIgnoreCase));
            }
            _logger.Information("Toggled monitored channel '{Identifier}' -> Enabled: {Enabled}", identifier, enable);
            return true;
        }
    }

    public async Task<System.Collections.Generic.List<TelegramDialogDto>> GetDialogsAsync()
    {
        if (_client == null || !IsConnected())
        {
            throw new TelegramConnectionException("Telegram client is not connected.");
        }

        var result = new System.Collections.Generic.List<TelegramDialogDto>();
        var dialogs = await _client.Messages_GetAllDialogs();

        var monitoredList = GetMonitoredChannels();

        foreach (var chatKv in dialogs.chats)
        {
            var chat = chatKv.Value;
            if (chat == null) continue;

            bool isChannel = false;
            bool isGroup = false;
            string username = string.Empty;

            if (chat is TL.Channel tlChannel)
            {
                isChannel = tlChannel.IsChannel;
                isGroup = tlChannel.IsGroup;
                username = tlChannel.username ?? string.Empty;
            }
            else if (chat is TL.Chat)
            {
                isGroup = true;
            }

            // Check if monitored
            bool monitored = IsChannelMonitored(chat);

            result.Add(new TelegramDialogDto
            {
                Id = chat.ID,
                Title = chat.Title ?? "Unnamed Chat",
                Username = username,
                IsChannel = isChannel,
                IsGroup = isGroup,
                IsMonitored = monitored
            });
        }

        return result;
    }

    async Task<List<DiscoveredTelegramChatDto>> ITelegramDiscoveryClient.GetDialogsAsync(CancellationToken ct)
    {
        var dialogs = await GetDialogsAsync();
        return dialogs.Select(d => new DiscoveredTelegramChatDto(
            d.Id,
            d.Title,
            d.Username,
            d.IsChannel,
            d.IsGroup
        )).ToList();
    }

    string ITelegramDiscoveryClient.GetCurrentState()
    {
        return CurrentState.ToString();
    }

    private bool IsChannelMonitored(TL.ChatBase chat)
    {
        if (chat == null) return false;

        if (_scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sourceRepo = scope.ServiceProvider.GetService<ITelegramSourceRepository>();
                if (sourceRepo != null)
                {
                    var source = sourceRepo.GetByChatIdAsync(chat.ID).GetAwaiter().GetResult();
                    if (source != null)
                    {
                        return source.IsEnabled && !source.IsPaused;
                    }
                    else
                    {
                        var configuredChannels = GetMonitoredChannels();
                        if (configuredChannels == null || configuredChannels.Count == 0)
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error checking active sources repository for ChatId {ChatId}. Falling back to configured channel list.", chat.ID);
            }
        }

        var monitoredChannels = GetMonitoredChannels();
        if (monitoredChannels == null || monitoredChannels.Count == 0)
        {
            return false;
        }

        foreach (var configuredChannel in monitoredChannels)
        {
            if (string.IsNullOrWhiteSpace(configuredChannel)) continue;

            // 1. Check ID match
            if (long.TryParse(configuredChannel, out var parsedId))
            {
                if (chat.ID == parsedId) return true;
            }

            // 2. Check title match
            if (!string.IsNullOrEmpty(chat.Title) &&
                chat.Title.Equals(configuredChannel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Check username match (if it is a Channel)
            if (chat is TL.Channel tlChannel &&
                !string.IsNullOrEmpty(tlChannel.username) &&
                tlChannel.username.Equals(configuredChannel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string? ConfigProvider(string what)
    {
        switch (what)
        {
            case "api_id":
                if (string.IsNullOrWhiteSpace(_options.ApiId))
                {
                    throw new InvalidTelegramConfigurationException("Telegram ApiId is not configured.");
                }
                return _options.ApiId;

            case "api_hash":
                if (string.IsNullOrWhiteSpace(_options.ApiHash))
                {
                    throw new InvalidTelegramConfigurationException("Telegram ApiHash is not configured.");
                }
                return _options.ApiHash;

            case "phone_number":
                if (string.IsNullOrWhiteSpace(_options.PhoneNumber))
                {
                    throw new InvalidTelegramConfigurationException("Telegram PhoneNumber is not configured.");
                }
                return _options.PhoneNumber;

            case "verification_code":
                var code = VerificationCodeProvider?.Invoke();
                if (string.IsNullOrWhiteSpace(code))
                {
                    code = Environment.GetEnvironmentVariable("TELEGRAM_VERIFICATION_CODE");
                }
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new TelegramAuthenticationException("Verification code is required but was not provided.");
                }
                return code;

            case "password":
                var pwd = PasswordProvider?.Invoke();
                if (string.IsNullOrWhiteSpace(pwd))
                {
                    pwd = Environment.GetEnvironmentVariable("TELEGRAM_PASSWORD");
                }
                if (string.IsNullOrWhiteSpace(pwd))
                {
                    throw new TelegramAuthenticationException("2FA Password is required but was not provided.");
                }
                return pwd;

            default:
                return null;
        }
    }

    private void ConfigureProxyIfNeeded(WTelegram.Client client)
    {
        if (client == null) return;

        if (!string.IsNullOrWhiteSpace(_options.ProxyUrl))
        {
            _logger.Information("Telegram client is configured to connect via proxy: {ProxyUrl}", _options.ProxyUrl);
            client.TcpHandler = (host, port) => CreateProxyTcpClientAsync(_options.ProxyUrl, host, port);
        }
    }

    private static async Task<System.Net.Sockets.TcpClient> CreateProxyTcpClientAsync(string proxyUrlStr, string targetHost, int targetPort)
    {
        if (!Uri.TryCreate(proxyUrlStr, UriKind.Absolute, out var proxyUri))
        {
            throw new InvalidOperationException($"Invalid Telegram proxy URL: '{proxyUrlStr}'");
        }

        var proxyHost = proxyUri.Host;
        var proxyPort = proxyUri.Port > 0 ? proxyUri.Port : (proxyUri.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase) ? 1080 : 8080);
        string? userInfo = proxyUri.UserInfo;
        string? username = null;
        string? password = null;
        if (!string.IsNullOrEmpty(userInfo))
        {
            var parts = userInfo.Split(':', 2);
            username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1) password = Uri.UnescapeDataString(parts[1]);
        }

        var tcpClient = new System.Net.Sockets.TcpClient();
        await tcpClient.ConnectAsync(proxyHost, proxyPort);

        var stream = tcpClient.GetStream();

        if (proxyUri.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
        {
            // SOCKS5 Protocol Handshake
            byte[] greeting;
            if (!string.IsNullOrEmpty(username))
            {
                greeting = new byte[] { 0x05, 0x02, 0x00, 0x02 }; // No auth (0x00) or Username/Password (0x02)
            }
            else
            {
                greeting = new byte[] { 0x05, 0x01, 0x00 }; // No auth (0x00)
            }
            await stream.WriteAsync(greeting, 0, greeting.Length);

            var response = new byte[2];
            int read = await stream.ReadAsync(response, 0, 2);
            if (read < 2 || response[0] != 0x05)
            {
                tcpClient.Dispose();
                throw new InvalidOperationException($"SOCKS5 proxy server rejected initial handshake from {proxyHost}:{proxyPort}.");
            }

            if (response[1] == 0x02) // Username/Password authentication
            {
                var userBytes = System.Text.Encoding.UTF8.GetBytes(username ?? "");
                var passBytes = System.Text.Encoding.UTF8.GetBytes(password ?? "");
                var authReq = new byte[3 + userBytes.Length + passBytes.Length];
                authReq[0] = 0x01; // Auth version
                authReq[1] = (byte)userBytes.Length;
                Buffer.BlockCopy(userBytes, 0, authReq, 2, userBytes.Length);
                authReq[2 + userBytes.Length] = (byte)passBytes.Length;
                Buffer.BlockCopy(passBytes, 0, authReq, 3 + userBytes.Length, passBytes.Length);

                await stream.WriteAsync(authReq, 0, authReq.Length);
                var authResp = new byte[2];
                int authRead = await stream.ReadAsync(authResp, 0, 2);
                if (authRead < 2 || authResp[1] != 0x00)
                {
                    tcpClient.Dispose();
                    throw new InvalidOperationException($"SOCKS5 proxy authentication failed for user '{username}'.");
                }
            }
            else if (response[1] != 0x00)
            {
                tcpClient.Dispose();
                throw new InvalidOperationException($"SOCKS5 proxy server selected unsupported auth method: 0x{response[1]:X2}.");
            }

            // SOCKS5 Connect Request
            var hostBytes = System.Text.Encoding.UTF8.GetBytes(targetHost);
            var connectReq = new byte[5 + hostBytes.Length + 2];
            connectReq[0] = 0x05; // VER
            connectReq[1] = 0x01; // CMD: Connect
            connectReq[2] = 0x00; // RSV
            connectReq[3] = 0x03; // ATYP: Domain name
            connectReq[4] = (byte)hostBytes.Length;
            Buffer.BlockCopy(hostBytes, 0, connectReq, 5, hostBytes.Length);
            connectReq[5 + hostBytes.Length] = (byte)(targetPort >> 8);
            connectReq[6 + hostBytes.Length] = (byte)(targetPort & 0xFF);

            await stream.WriteAsync(connectReq, 0, connectReq.Length);

            var connectResp = new byte[10];
            int connRead = await stream.ReadAsync(connectResp, 0, 4);
            if (connRead < 4 || connectResp[1] != 0x00)
            {
                tcpClient.Dispose();
                throw new InvalidOperationException($"SOCKS5 proxy connection to {targetHost}:{targetPort} failed with status code 0x{connectResp[1]:X2}.");
            }

            int remainingToRead = connectResp[3] switch
            {
                0x01 => 6, // IPv4 (4 bytes) + port (2 bytes)
                0x04 => 18, // IPv6 (16 bytes) + port (2 bytes)
                0x03 => connectResp[4] + 2 + 1 - 4, // Domain length + 2 bytes port
                _ => 6
            };
            if (remainingToRead > 0)
            {
                var dummy = new byte[remainingToRead];
                await stream.ReadAsync(dummy, 0, remainingToRead);
            }

            return tcpClient;
        }
        else
        {
            // HTTP / HTTPS CONNECT Proxy Tunnel
            var connectHeader = $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\n";
            if (!string.IsNullOrEmpty(username))
            {
                var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                connectHeader += $"Proxy-Authorization: Basic {auth}\r\n";
            }
            connectHeader += "\r\n";

            var headerBytes = System.Text.Encoding.UTF8.GetBytes(connectHeader);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            var statusLine = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(statusLine) || !statusLine.Contains(" 200 "))
            {
                tcpClient.Dispose();
                throw new InvalidOperationException($"HTTP Proxy CONNECT to {targetHost}:{targetPort} failed: '{statusLine}'");
            }

            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                // Consume headers until empty line
            }

            return tcpClient;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.Dispose();
            _disposed = true;
        }
    }
}
