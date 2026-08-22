using System;

namespace TradingBot.Exchange.Bybit;

public class BybitOptions
{
    public const string DemoBaseUrl = "https://api-demo.bybit.com";
    public const string TestnetBaseUrl = "https://api-testnet.bybit.com";
    public const string MainnetBaseUrl = "https://api.bybit.com";

    public const string DemoPublicWsUrl = "wss://stream.bybit.com/v5/public";
    public const string TestnetPublicWsUrl = "wss://stream-testnet.bybit.com/v5/public";
    public const string MainnetPublicWsUrl = "wss://stream.bybit.com/v5/public";

    public const string DemoPrivateWsUrl = "wss://stream-demo.bybit.com/v5/private";
    public const string TestnetPrivateWsUrl = "wss://stream-testnet.bybit.com/v5/private";
    public const string MainnetPrivateWsUrl = "wss://stream.bybit.com/v5/private";

    public string Environment { get; set; } = "Demo";

    public string DemoApiKey { get; set; } = string.Empty;
    public string DemoApiSecret { get; set; } = string.Empty;

    public string MainnetApiKey { get; set; } = string.Empty;
    public string MainnetApiSecret { get; set; } = string.Empty;

    public int RecvWindow { get; set; } = 5000;
    public string ProxyUrl { get; set; } = string.Empty;

    public bool IsDemo => string.Equals(Environment, "Demo", StringComparison.OrdinalIgnoreCase);
    public bool IsTestnet => string.Equals(Environment, "Testnet", StringComparison.OrdinalIgnoreCase);
    public bool IsMainnet => string.Equals(Environment, "Mainnet", StringComparison.OrdinalIgnoreCase) || string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);

    public string GetBaseUrl()
    {
        return GetBaseUrl(Environment);
    }

    public static string GetBaseUrl(string? environment)
    {
        if (string.Equals(environment, "Mainnet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return MainnetBaseUrl;
        }

        if (string.Equals(environment, "Testnet", StringComparison.OrdinalIgnoreCase))
        {
            return TestnetBaseUrl;
        }

        // Default or "Demo"
        return DemoBaseUrl;
    }

    public string GetApiKey()
    {
        return GetApiKey(Environment);
    }

    public string GetApiKey(string? environment)
    {
        if (string.Equals(environment, "Mainnet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(MainnetApiKey) ? MainnetApiKey : DemoApiKey;
        }

        return !string.IsNullOrEmpty(DemoApiKey) ? DemoApiKey : MainnetApiKey;
    }

    public string GetApiSecret()
    {
        return GetApiSecret(Environment);
    }

    public string GetApiSecret(string? environment)
    {
        if (string.Equals(environment, "Mainnet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(MainnetApiSecret) ? MainnetApiSecret : DemoApiSecret;
        }

        return !string.IsNullOrEmpty(DemoApiSecret) ? DemoApiSecret : MainnetApiSecret;
    }

    public string GetPublicWebSocketUrl(string category = "spot")
    {
        return GetPublicWebSocketUrl(Environment, category);
    }

    public static string GetPublicWebSocketUrl(string? environment, string category = "spot")
    {
        var cat = string.IsNullOrWhiteSpace(category) ? "spot" : category.ToLowerInvariant();

        if (string.Equals(environment, "Testnet", StringComparison.OrdinalIgnoreCase))
        {
            return $"wss://stream-testnet.bybit.com/v5/public/{cat}";
        }

        // For both Demo and Mainnet/Production, Bybit serves Public Market Data WS from stream.bybit.com
        return $"wss://stream.bybit.com/v5/public/{cat}";
    }

    public string GetPrivateWebSocketUrl()
    {
        return GetPrivateWebSocketUrl(Environment);
    }

    public static string GetPrivateWebSocketUrl(string? environment)
    {
        if (string.Equals(environment, "Mainnet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return MainnetPrivateWsUrl;
        }

        if (string.Equals(environment, "Testnet", StringComparison.OrdinalIgnoreCase))
        {
            return TestnetPrivateWsUrl;
        }

        return DemoPrivateWsUrl;
    }
}
