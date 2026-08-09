using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using WTelegram;
using TL;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Client;

public class TelegramClientService : ITelegramClient, IDisposable
{
    private readonly TelegramOptions _options;
    private readonly ITelegramSessionManager _sessionManager;
    private readonly ITelegramMessageReceiver _messageReceiver;
    private readonly ILogger _logger;
    private WTelegram.Client? _client;
    private WTelegram.UpdateManager? _updateManager;
    private TelegramConnectionState _currentState = TelegramConnectionState.Disconnected;
    private readonly object _stateLock = new();
    private bool _disposed;

    public Func<string>? VerificationCodeProvider { get; set; }
    public Func<string>? PasswordProvider { get; set; }

    public TelegramClientService(
        IOptions<TelegramOptions> options,
        ITelegramSessionManager sessionManager,
        ITelegramMessageReceiver messageReceiver)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
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

    private bool IsChannelMonitored(TL.ChatBase chat)
    {
        if (chat == null) return false;

        var monitoredChannels = _options.Channels;
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.Dispose();
            _disposed = true;
        }
    }
}
