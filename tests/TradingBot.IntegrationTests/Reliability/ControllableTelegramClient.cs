using System;
using System.Threading.Tasks;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.IntegrationTests.Reliability;

public class ControllableTelegramClient : ITelegramClient
{
    private readonly FailureSimulator _simulator;
    private TelegramConnectionState _state = TelegramConnectionState.Disconnected;
    private int _messageCount = 0;

    public ControllableTelegramClient(FailureSimulator simulator)
    {
        _simulator = simulator;
    }

    public int MessageCount => _messageCount;

    public Task ConnectAsync()
    {
        var key = "Telegram";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            _state = TelegramConnectionState.Error;
            _simulator.HandleFailureType(failureType, "ConnectAsync");
        }

        _state = TelegramConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _state = TelegramConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public bool IsConnected()
    {
        return _state == TelegramConnectionState.Connected || _state == TelegramConnectionState.Listening;
    }

    public TelegramConnectionState CurrentState => _state;

    public void SetState(TelegramConnectionState state)
    {
        _state = state;
    }

    public Task InitializeListeningAsync()
    {
        var key = "Telegram";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            _state = TelegramConnectionState.Error;
            _simulator.HandleFailureType(failureType, "InitializeListeningAsync");
        }

        _state = TelegramConnectionState.Listening;
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(long chatId, string message)
    {
        var key = "Telegram";
        if (_simulator.ShouldFail(key, out var failureType))
        {
            _simulator.HandleFailureType(failureType, "SendMessageAsync");
        }

        _messageCount++;
        return Task.CompletedTask;
    }
}
