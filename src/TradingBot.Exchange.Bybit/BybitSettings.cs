using System.Collections.Generic;
using TradingBot.Application.Interfaces;

namespace TradingBot.Exchange.Bybit;

public class BybitSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseSandbox { get; set; } = true;
    public string Environment { get; set; } = "Testnet";
    public int RecvWindow { get; set; } = 5000;
    public string ProxyUrl { get; set; } = string.Empty;
    public List<BybitAccountSettings> Accounts { get; set; } = new();
}
