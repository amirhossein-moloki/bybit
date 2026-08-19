using System.Collections.Generic;
using TradingBot.Application.Interfaces;

namespace TradingBot.Exchange.Bybit;

public class BybitSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    public string DemoApiKey { get; set; } = string.Empty;
    public string DemoApiSecret { get; set; } = string.Empty;

    public string MainnetApiKey { get; set; } = string.Empty;
    public string MainnetApiSecret { get; set; } = string.Empty;

    public bool UseSandbox { get; set; } = false;
    public string Environment { get; set; } = "Demo";
    public int RecvWindow { get; set; } = 5000;
    public string ProxyUrl { get; set; } = string.Empty;
    public List<BybitAccountSettings> Accounts { get; set; } = new();

    public string EffectiveApiKey => !string.IsNullOrEmpty(ApiKey)
        ? ApiKey
        : (string.Equals(Environment, "Mainnet", System.StringComparison.OrdinalIgnoreCase) || string.Equals(Environment, "Production", System.StringComparison.OrdinalIgnoreCase)
            ? MainnetApiKey
            : DemoApiKey);

    public string EffectiveApiSecret => !string.IsNullOrEmpty(ApiSecret)
        ? ApiSecret
        : (string.Equals(Environment, "Mainnet", System.StringComparison.OrdinalIgnoreCase) || string.Equals(Environment, "Production", System.StringComparison.OrdinalIgnoreCase)
            ? MainnetApiSecret
            : DemoApiSecret);
}
