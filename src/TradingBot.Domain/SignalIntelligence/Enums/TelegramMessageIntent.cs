namespace TradingBot.Domain.SignalIntelligence.Enums;

public enum TelegramMessageIntent
{
    EntrySignal = 0,
    PositionManagement = 1,
    ExitSignal = 2,
    MarketAnalysis = 3,
    Informational = 4,
    Noise = 5,
    Unknown = 6
}
