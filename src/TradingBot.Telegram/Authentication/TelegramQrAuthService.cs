using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Authentication;

public class TelegramQrAuthService : ITelegramQrAuthService
{
    private readonly ITelegramClient _client;
    private readonly ITelegramSessionManager _sessionManager;
    private readonly TelegramOptions _options;
    private readonly ILogger _logger;

    private readonly object _lock = new();
    private QrSessionState? _currentSession;

    public TelegramQrAuthService(
        ITelegramClient client,
        ITelegramSessionManager sessionManager,
        IOptions<TelegramOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = Log.ForContext<TelegramQrAuthService>();
    }

    public async Task<TelegramStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;

        bool hasSession = _sessionManager.SessionExists();
        bool isConnected = _client.IsConnected();
        var account = _client.GetConnectedAccount();

        string statusStr = "NotConnected";
        if (isConnected && account != null)
        {
            statusStr = _client.CurrentState == TelegramConnectionState.Listening ? "Active" : "Connected";
        }
        else if (hasSession)
        {
            statusStr = _client.CurrentState.ToString();
        }
        else
        {
            statusStr = "NotConnected";
        }

        return new TelegramStatusDto
        {
            Connected = isConnected && account != null,
            Status = statusStr,
            Account = account
        };
    }

    public async Task<TelegramQrStartResultDto> StartQrAuthAsync(CancellationToken ct = default)
    {
        QrSessionState? sessionToCancel = null;
        QrSessionState newSession;

        lock (_lock)
        {
            if (_currentSession != null && !_currentSession.IsTerminal)
            {
                sessionToCancel = _currentSession;
            }

            var sessionId = Guid.NewGuid().ToString("N");
            newSession = new QrSessionState(sessionId);
            _currentSession = newSession;
        }

        if (sessionToCancel != null)
        {
            sessionToCancel.Cancel();
        }

        _logger.Information("Starting Telegram QR Login session {SessionId}", newSession.SessionId);

        // Start background task running LoginWithQrCodeAsync
        newSession.AuthTask = Task.Run(async () => await RunQrAuthLoopAsync(newSession), CancellationToken.None);

        // Await first QR code data received from WTelegram qrDisplay callback (or timeout 10 seconds)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

        try
        {
            await newSession.FirstQrReceived.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (string.IsNullOrEmpty(newSession.QrData))
            {
                lock (newSession)
                {
                    newSession.Status = "Failed";
                    newSession.Error = "Timed out waiting for Telegram QR code initialization.";
                }
                throw new InvalidOperationException("Timed out waiting for Telegram QR code initialization.");
            }
        }

        lock (newSession)
        {
            return new TelegramQrStartResultDto
            {
                SessionId = newSession.SessionId,
                QrData = newSession.QrData ?? string.Empty,
                ExpiresAt = newSession.ExpiresAt?.ToString("o") ?? DateTime.UtcNow.AddSeconds(30).ToString("o")
            };
        }
    }

    public async Task<TelegramQrStatusDto> GetQrStatusAsync(string? sessionId = null, CancellationToken ct = default)
    {
        await Task.CompletedTask;

        QrSessionState? targetSession = null;
        lock (_lock)
        {
            if (string.IsNullOrEmpty(sessionId) || (_currentSession != null && _currentSession.SessionId == sessionId))
            {
                targetSession = _currentSession;
            }
        }

        if (targetSession == null)
        {
            return new TelegramQrStatusDto
            {
                SessionId = sessionId ?? string.Empty,
                Status = "Failed",
                Error = "Session not found."
            };
        }

        // Check if QR token time expired without scan
        lock (targetSession)
        {
            if (targetSession.Status == "WaitingForScan" && targetSession.ExpiresAt.HasValue && DateTime.UtcNow > targetSession.ExpiresAt.Value.AddSeconds(5))
            {
                if (targetSession.CancellationTokenSource.IsCancellationRequested || targetSession.AuthTask?.IsCompleted == true)
                {
                    targetSession.Status = "Expired";
                }
            }

            return new TelegramQrStatusDto
            {
                SessionId = targetSession.SessionId,
                Status = targetSession.Status,
                QrData = targetSession.QrData,
                ExpiresAt = targetSession.ExpiresAt?.ToString("o"),
                Account = targetSession.Account,
                Error = targetSession.Error
            };
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        _logger.Information("Logging out Telegram account and invalidating session.");

        lock (_lock)
        {
            _currentSession?.Cancel();
            _currentSession = null;
        }

        await _client.DisconnectAsync();
        _sessionManager.DeleteSession();
        _client.SetState(TelegramConnectionState.NotConnected);

        _logger.Information("Telegram logged out successfully.");
    }

    private async Task RunQrAuthLoopAsync(QrSessionState session)
    {
        try
        {
            _client.SetState(TelegramConnectionState.Authenticating);

            var user = await _client.LoginWithQrCodeAsync(qrUrl =>
            {
                lock (session)
                {
                    _logger.Information("Received Telegram QR login payload: {QrUrl}", qrUrl);
                    session.QrData = qrUrl;
                    session.ExpiresAt = DateTime.UtcNow.AddSeconds(30);
                    if (session.Status != "ScanDetected" && session.Status != "Authenticating")
                    {
                        session.Status = "WaitingForScan";
                    }
                    session.FirstQrReceived.TrySetResult(true);
                }
            }, session.CancellationTokenSource.Token);

            if (user != null)
            {
                lock (session)
                {
                    session.Status = "Connected";
                    session.Account = new TelegramAccountDto
                    {
                        Id = user.id,
                        Username = user.username,
                        FirstName = user.first_name,
                        LastName = user.last_name,
                        Phone = user.phone
                    };
                }

                _client.SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram QR Login completed successfully for user {UserId} (@{Username})", user.id, user.username);
            }
            else
            {
                lock (session)
                {
                    session.Status = "Failed";
                    session.Error = "QR Login failed. User object returned null.";
                }
                _client.SetState(TelegramConnectionState.AuthenticationFailed);
            }
        }
        catch (OperationCanceledException)
        {
            lock (session)
            {
                if (session.Status != "Connected")
                {
                    session.Status = "Expired";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred during Telegram QR Login session {SessionId}", session.SessionId);
            lock (session)
            {
                session.Status = "Failed";
                session.Error = ex.Message;
            }
            session.FirstQrReceived.TrySetException(ex);
            _client.SetState(TelegramConnectionState.AuthenticationFailed);
        }
    }

    private class QrSessionState
    {
        public string SessionId { get; }
        public string Status { get; set; } = "WaitingForScan";
        public string? QrData { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public TelegramAccountDto? Account { get; set; }
        public string? Error { get; set; }
        public TaskCompletionSource<bool> FirstQrReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource CancellationTokenSource { get; } = new();
        public Task? AuthTask { get; set; }

        public bool IsTerminal => Status is "Connected" or "Expired" or "Failed";

        public QrSessionState(string sessionId)
        {
            SessionId = sessionId;
        }

        public void Cancel()
        {
            try
            {
                CancellationTokenSource.Cancel();
            }
            catch { }
        }
    }
}
