namespace TradingBot.Application.Trading.Execution.Models;

public class InstrumentRules
{
    public string Symbol { get; set; } = string.Empty;
    public decimal TickSize { get; set; }
    public decimal QuantityStep { get; set; }
    public decimal MinQuantity { get; set; }
    public decimal? MaxQuantity { get; set; }
    public decimal MinNotional { get; set; }
    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }
}
