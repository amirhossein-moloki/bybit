namespace TradingBot.Parser.Configuration;

public class ValidationOptions
{
    public const string SectionName = "Validation";

    public bool RequireStopLoss { get; set; } = true;
    public bool RequireTakeProfit { get; set; } = true;
    public int MaximumLeverage { get; set; } = 100;
    public bool RejectUnknownSymbols { get; set; } = true;
}
