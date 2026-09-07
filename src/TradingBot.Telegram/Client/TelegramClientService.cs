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
    private DateTime? _floodWaitUntil;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly System.Collections.Generic.HashSet<string> _dynamicMonitoredChannels = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public Func<string>? PhoneNumberProvider { get; set; }
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

        WTelegram.Helpers.Log = (level, message) =>
        {
            switch (level)
            {
                case 1:
                case 2:
                    _logger.Debug("WTelegram [{Level}]: {Message}", level, message);
                    break;
                case 3:
                    _logger.Information("WTelegram [{Level}]: {Message}", level, message);
                    break;
                case 4:
                    _logger.Warning("WTelegram [{Level}]: {Message}", level, message);
                    break;
                case 5:
                    _logger.Error("WTelegram [{Level}]: {Message}", level, message);
                    break;
                default:
                    _logger.Debug("WTelegram [{Level}]: {Message}", level, message);
                    break;
            }
        };
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

    public DateTime? FloodWaitUntil
    {
        get
        {
            lock (_stateLock)
            {
                return _floodWaitUntil;
            }
        }
    }

    public void SetFloodWait(int seconds)
    {
        lock (_stateLock)
        {
            if (seconds <= 0)
            {
                _floodWaitUntil = null;
                return;
            }

            var until = DateTime.UtcNow.AddSeconds(seconds);
            if (!_floodWaitUntil.HasValue || until > _floodWaitUntil.Value)
            {
                _floodWaitUntil = until;
                _logger.Warning("Telegram client entering FLOOD_WAIT cooldown for {Seconds}s until {UntilUtc} UTC.", seconds, until.ToString("o"));
            }
        }
    }

    public bool IsInFloodWait(out TimeSpan remaining)
    {
        lock (_stateLock)
        {
            if (_floodWaitUntil.HasValue)
            {
                var diff = _floodWaitUntil.Value - DateTime.UtcNow;
                if (diff > TimeSpan.Zero)
                {
                    remaining = diff;
                    return true;
                }
                _floodWaitUntil = null;
            }

            remaining = TimeSpan.Zero;
            return false;
        }
    }

    public async Task ConnectAsync()
    {
        if (!_options.Enabled)
        {
            _logger.Warning("Telegram integration is disabled in configuration.");
            return;
        }

        if (IsInFloodWait(out var remaining))
        {
            _logger.Warning("Telegram ConnectAsync aborted due to active FLOOD_WAIT cooldown ({RemainingSeconds}s remaining).", Math.Ceiling(remaining.TotalSeconds));
            throw new TelegramConnectionException($"Telegram client is in FLOOD_WAIT cooldown for another {Math.Ceiling(remaining.TotalSeconds)} seconds.");
        }

        await _connectLock.WaitAsync();
        try
        {
            if (IsConnected() && _client?.User != null)
            {
                _logger.Information("Telegram client is already connected and active user session is loaded.");
                return;
            }

            SetState(TelegramConnectionState.Connecting);
            _logger.Information("Telegram connection started");

            if (_client == null || (_client.User == null && _sessionManager.SessionExists()))
            {
                if (_client != null)
                {
                    try { _client.Dispose(); } catch { }
                    _client = null;
                }
                var sessionStream = _sessionManager.LoadSession();
                _client = new WTelegram.Client(ConfigProvider, sessionStream);
            }

            if (_client.Disconnected)
            {
                await _client.ConnectAsync();
            }

            if (_client.User == null)
            {
                await _client.LoginUserIfNeeded();
            }

            if (_client.User != null)
            {
                SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram Connected and user session loaded (@{Username})", _client.User.username);
            }
            else
            {
                SetState(TelegramConnectionState.RequiresAuthentication);
                _logger.Warning("Telegram Connected, but user session requires authentication.");
            }
        }
        catch (Exception ex)
        {
            SetState(TelegramConnectionState.Error);
            _logger.Error(ex, "Failed to connect to Telegram.");
            throw new TelegramConnectionException("Failed to establish connection to Telegram.", ex);
        }
        finally
        {
            _connectLock.Release();
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
        if (!await IsChannelMonitoredAsync(chat))
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
            if (_sessionManager.SessionExists())
            {
                _logger.Information("GetDialogsAsync called while disconnected. Attempting auto-connection using existing session...");
                try
                {
                    await ConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to auto-connect Telegram client during GetDialogsAsync.");
                }
            }
        }

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
            bool monitored = await IsChannelMonitoredAsync(chat);

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

    private async Task<bool> IsChannelMonitoredAsync(TL.ChatBase chat)
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
                    var source = await sourceRepo.GetByChatIdAsync(chat.ID);
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
                var phone = PhoneNumberProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    return phone;
                }
                if (!string.IsNullOrWhiteSpace(_options.PhoneNumber))
                {
                    return _options.PhoneNumber;
                }
                return null;

            case "verification_code":
                var code = VerificationCodeProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }
                code = Environment.GetEnvironmentVariable("TELEGRAM_VERIFICATION_CODE");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }
                return null;

            case "password":
                var pwd = PasswordProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(pwd))
                {
                    return pwd;
                }
                pwd = Environment.GetEnvironmentVariable("TELEGRAM_PASSWORD");
                if (!string.IsNullOrWhiteSpace(pwd))
                {
                    return pwd;
                }
                return null;

            case "socks_ip":
            case "socks_port":
            case "socks_username":
            case "socks_password":
            case "http_proxy":
            case "proxy_ip":
            case "proxy_port":
            case "proxy_username":
            case "proxy_password":
                if (string.IsNullOrWhiteSpace(_options.ProxyUrl) || !Uri.TryCreate(_options.ProxyUrl, UriKind.Absolute, out var parsedProxyUri))
                {
                    return null;
                }

                var scheme = parsedProxyUri.Scheme.ToLowerInvariant();
                bool isSocks = scheme is "socks" or "socks5" or "socks5h";
                bool isHttp = scheme is "http" or "https";

                if (what is "socks_ip" && (isSocks || !isHttp))
                {
                    return parsedProxyUri.Host;
                }
                if (what is "socks_port" && (isSocks || !isHttp) && parsedProxyUri.Port > 0)
                {
                    return parsedProxyUri.Port.ToString();
                }
                if (what is "socks_username" && (isSocks || !isHttp) && !string.IsNullOrEmpty(parsedProxyUri.UserInfo))
                {
                    var parts = parsedProxyUri.UserInfo.Split(':');
                    return parts.Length > 0 ? Uri.UnescapeDataString(parts[0]) : null;
                }
                if (what is "socks_password" && (isSocks || !isHttp) && !string.IsNullOrEmpty(parsedProxyUri.UserInfo))
                {
                    var parts = parsedProxyUri.UserInfo.Split(':');
                    return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
                }

                if (what is "http_proxy" && isHttp)
                {
                    return _options.ProxyUrl;
                }
                if (what is "proxy_ip" && isHttp)
                {
                    return parsedProxyUri.Host;
                }
                if (what is "proxy_port" && isHttp && parsedProxyUri.Port > 0)
                {
                    return parsedProxyUri.Port.ToString();
                }
                if (what is "proxy_username" && isHttp && !string.IsNullOrEmpty(parsedProxyUri.UserInfo))
                {
                    var parts = parsedProxyUri.UserInfo.Split(':');
                    return parts.Length > 0 ? Uri.UnescapeDataString(parts[0]) : null;
                }
                if (what is "proxy_password" && isHttp && !string.IsNullOrEmpty(parsedProxyUri.UserInfo))
                {
                    var parts = parsedProxyUri.UserInfo.Split(':');
                    return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
                }

                return null;

            default:
                return null;
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
