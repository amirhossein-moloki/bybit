using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Telegram.Client;

namespace TradingBot.Telegram.Authentication;

public class TelegramAuthService : ITelegramAuthenticationService
{
    private readonly ITelegramClient _client;
    private readonly TelegramOptions _options;
    private readonly ILogger _logger;

    public TelegramAuthService(
        ITelegramClient client,
        IOptions<TelegramOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = Log.ForContext<TelegramAuthService>();
    }

    public async Task AuthenticateAsync()
    {
        if (!_options.Enabled)
        {
            _logger.Warning("Telegram integration is disabled. Skipping authentication.");
            return;
        }

        if (_client is not TelegramClientService clientService)
        {
            throw new TelegramAuthenticationException("ITelegramClient implementation is not of type TelegramClientService.");
        }

        try
        {
            // Ensure connection is established
            if (!clientService.IsConnected())
            {
                clientService.SetState(TelegramConnectionState.Connecting);
                await clientService.ConnectAsync();
            }

            var underlyingClient = clientService.UnderlyingClient;
            if (underlyingClient == null)
            {
                clientService.SetState(TelegramConnectionState.Error);
                throw new TelegramAuthenticationException("Underlying WTelegram client is not initialized.");
            }

            clientService.SetState(TelegramConnectionState.Authenticating);
            _logger.Information("Beginning Telegram login flow...");

            // Call LoginUserIfNeeded to perform authentication flow
            var user = await underlyingClient.LoginUserIfNeeded();

            if (user != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Authentication successful");
            }
            else
            {
                clientService.SetState(TelegramConnectionState.AuthenticationFailed);
                throw new TelegramAuthenticationException("Telegram login failed: returned user was null.");
            }
        }
        catch (Exception ex) when (ex is not TelegramAuthenticationException)
        {
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            _logger.Error(ex, "Telegram authentication failed.");
            throw new TelegramAuthenticationException("Failed to complete Telegram authentication.", ex);
        }
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        if (!_options.Enabled) return false;

        if (_client is not TelegramClientService clientService) return false;

        var underlyingClient = clientService.UnderlyingClient;
        if (underlyingClient == null) return false;

        try
        {
            return underlyingClient.User != null;
        }
        catch
        {
            return false;
        }
    }
}
