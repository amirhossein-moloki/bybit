using TradingBot.Domain.Enums;

namespace TradingBot.Application.Interfaces;

public interface ITradingGate
{
    ApplicationState CurrentState { get; }
    bool IsTradingEnabled { get; }
    void SetState(ApplicationState state);
    void EnableTrading();
    void DisableTrading();
}
