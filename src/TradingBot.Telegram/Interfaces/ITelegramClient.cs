using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramClient
{
    Task ConnectAsync();
    Task DisconnectAsync();
    bool IsConnected();
    TelegramConnectionState CurrentState { get; }
    void SetState(TelegramConnectionState state);
    Task InitializeListeningAsync();
    Task SendMessageAsync(long chatId, string message);
    Task<TL.User?> LoginWithQrCodeAsync(Action<string> qrDisplay, CancellationToken ct = default);
    TelegramAccountDto? GetConnectedAccount();
}
