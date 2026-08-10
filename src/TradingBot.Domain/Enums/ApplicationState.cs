namespace TradingBot.Domain.Enums;

public enum ApplicationState
{
    Starting,
    Initializing,
    Recovering,
    Ready,
    Degraded,
    Stopping,
    Stopped,
    Failed
}
