using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Client;

public class TelegramClientService : ITelegramClient, IDisposable
{
    private readonly TelegramOptions _options;
    private readonly ITelegramSessionManager _sessionManager;
    private readonly ILogger _logger;
    private WTelegram.Client? _client;
    private TelegramConnectionState _currentState = TelegramConnectionState.Disconnected;
    private bool _disposed;

    public Func<string>? VerificationCodeProvider { get; set; }
    public Func<string>? PasswordProvider { get; set; }

    public TelegramClientService(
        IOptions<TelegramOptions> options,
        ITelegramSessionManager sessionManager)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = Log.ForContext<TelegramClientService>();
    }

    public TelegramConnectionState CurrentState => _currentState;

    public void SetState(TelegramConnectionState state)
    {
        _currentState = state;
        _logger.Information("Telegram connection state changed to {State}", state);
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

            // Note: LoginUserIfNeeded will be called during AuthenticateAsync inside TelegramAuthService,
            // but just connecting establishes the socket. WTelegram.Client.ConnectAsync establishes the connection.
            SetState(TelegramConnectionState.Connected);
            _logger.Information("Telegram connected successfully");
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
            SetState(TelegramConnectionState.Disconnected);
            _logger.Information("Telegram client disconnected.");
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
        return _currentState == TelegramConnectionState.Connected && _client != null;
    }

    public WTelegram.Client? UnderlyingClient => _client;

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
