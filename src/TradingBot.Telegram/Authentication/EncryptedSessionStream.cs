using System;
using System.IO;
using Serilog;
using TradingBot.Application.Interfaces;

namespace TradingBot.Telegram.Authentication;

public class EncryptedSessionStream : MemoryStream
{
    private readonly string _sessionPath;
    private readonly IEncryptionService _encryptionService;
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public EncryptedSessionStream(string sessionPath, IEncryptionService encryptionService)
    {
        _sessionPath = sessionPath ?? throw new ArgumentNullException(nameof(sessionPath));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _logger = Log.ForContext<EncryptedSessionStream>();

        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_sessionPath))
                {
                    var encryptedText = File.ReadAllText(_sessionPath);
                    if (!string.IsNullOrWhiteSpace(encryptedText))
                    {
                        var decryptedText = _encryptionService.Decrypt(encryptedText);
                        if (!string.IsNullOrEmpty(decryptedText))
                        {
                            var decryptedBytes = Convert.FromBase64String(decryptedText);
                            base.Write(decryptedBytes, 0, decryptedBytes.Length);
                            base.Position = 0;
                            _logger.Information("Session restored successfully from encrypted storage.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load and decrypt existing Telegram session file.");
                // We do not rethrow here because WTelegramClient will fall back to creating a new session
                // if the stream is empty/corrupt, but we log the error.
            }
        }
    }

    private void SaveToDisk()
    {
        lock (_lock)
        {
            try
            {
                var decryptedBytes = base.ToArray();
                if (decryptedBytes.Length == 0) return;

                var decryptedText = Convert.ToBase64String(decryptedBytes);
                var encryptedText = _encryptionService.Encrypt(decryptedText);

                var directory = Path.GetDirectoryName(_sessionPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_sessionPath, encryptedText);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to encrypt and save Telegram session to disk.");
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        base.Write(buffer, offset, count);
        SaveToDisk();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
        SaveToDisk();
    }

    public override void WriteByte(byte value)
    {
        base.WriteByte(value);
        SaveToDisk();
    }

    public override void SetLength(long value)
    {
        base.SetLength(value);
        SaveToDisk();
    }

    public override void Flush()
    {
        base.Flush();
        SaveToDisk();
    }
}
