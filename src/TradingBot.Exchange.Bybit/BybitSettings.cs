namespace TradingBot.Exchange.Bybit;

public class BybitSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseSandbox { get; set; } = true;
    public string Environment { get; set; } = "Testnet";
    public int RecvWindow { get; set; } = 5000;
}
