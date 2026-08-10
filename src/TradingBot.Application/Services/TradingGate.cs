using System;
using TradingBot.Application.Interfaces;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Services;

public class TradingGate : ITradingGate
{
    private readonly object _lock = new();
    private ApplicationState _currentState = ApplicationState.Starting;
    private bool _isTradingEnabled = false;

    public ApplicationState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    public bool IsTradingEnabled
    {
        get
        {
            lock (_lock)
            {
                return _isTradingEnabled;
            }
        }
    }

    public void SetState(ApplicationState state)
    {
        lock (_lock)
        {
            _currentState = state;
            // Trading should only be enabled in Ready state if expressly enabled
            if (state != ApplicationState.Ready)
            {
                _isTradingEnabled = false;
            }
        }
    }

    public void EnableTrading()
    {
        lock (_lock)
        {
            if (_currentState == ApplicationState.Ready || _currentState == ApplicationState.Degraded)
            {
                _isTradingEnabled = true;
            }
            else
            {
                throw new InvalidOperationException($"Cannot enable trading when application is in {_currentState} state.");
            }
        }
    }

    public void DisableTrading()
    {
        lock (_lock)
        {
            _isTradingEnabled = false;
        }
    }
}
