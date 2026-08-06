using System;
using System.IO;
using Microsoft.Extensions.Options;
using Serilog;
using TradingBot.Application.Interfaces;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Telegram.Authentication;

public class TelegramSessionManager : ITelegramSessionManager
{
    private readonly TelegramOptions _options;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger _logger;

    public TelegramSessionManager(
        IOptions<TelegramOptions> options,
        IEncryptionService encryptionService)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _logger = Log.ForContext<TelegramSessionManager>();
    }

    public Stream LoadSession()
    {
        try
        {
            var sessionPath = _options.SessionPath;
            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                throw new TelegramSessionException("SessionPath is not configured.");
            }

            return new EncryptedSessionStream(sessionPath, _encryptionService);
        }
        catch (Exception ex) when (ex is not TelegramSessionException)
        {
            _logger.Error(ex, "Failed to load encrypted session.");
            throw new TelegramSessionException("Failed to load or initialize the encrypted session.", ex);
        }
    }

    public void SaveSession(Stream sessionStream)
    {
        if (sessionStream is EncryptedSessionStream encryptedStream)
        {
            try
            {
                encryptedStream.Flush();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save encrypted session stream.");
                throw new TelegramSessionException("Failed to save the encrypted session.", ex);
            }
        }
        else
        {
            throw new TelegramSessionException("Invalid session stream type. Expected EncryptedSessionStream.");
        }
    }

    public bool SessionExists()
    {
        return !string.IsNullOrWhiteSpace(_options.SessionPath) && File.Exists(_options.SessionPath);
    }

    public void DeleteSession()
    {
        try
        {
            if (SessionExists())
            {
                File.Delete(_options.SessionPath);
                _logger.Information("Telegram session deleted successfully.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete session file.");
            throw new TelegramSessionException("Failed to delete session file.", ex);
        }
    }
}
