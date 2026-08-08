namespace TradingBot.Application.Trading.Execution.Models;

public class TrailingStopSettings
{
    public bool Enabled { get; set; } = true;
    public decimal? ActivationPrice { get; set; }
    public decimal? Distance { get; set; }
    public decimal? Percentage { get; set; }
    public decimal Step { get; set; } = 0m;
}
