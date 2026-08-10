using System;

namespace TradingBot.Application.Configuration;

public class StartupShutdownOptions
{
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool RequireExchangeSync { get; set; } = true;

    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool DrainPendingOperations { get; set; } = true;

    public bool RequireDatabase { get; set; } = true;
    public bool RequireExchange { get; set; } = true;
    public bool RequireRecovery { get; set; } = true;
}
