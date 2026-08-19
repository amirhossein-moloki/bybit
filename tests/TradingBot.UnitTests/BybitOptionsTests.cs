using FluentAssertions;
using TradingBot.Exchange.Bybit;
using Xunit;

namespace TradingBot.UnitTests;

public class BybitOptionsTests
{
    [Theory]
    [InlineData("Demo", "https://api-demo.bybit.com")]
    [InlineData("Mainnet", "https://api.bybit.com")]
    [InlineData("Production", "https://api.bybit.com")]
    [InlineData("demo", "https://api-demo.bybit.com")]
    [InlineData("mainnet", "https://api.bybit.com")]
    [InlineData(null, "https://api-demo.bybit.com")]
    [InlineData("Unknown", "https://api-demo.bybit.com")]
    public void GetBaseUrl_ShouldReturnCorrectUrl(string? env, string expectedUrl)
    {
        var url = BybitOptions.GetBaseUrl(env);
        url.Should().Be(expectedUrl);
    }

    [Theory]
    [InlineData("Demo", "wss://stream-demo.bybit.com/v5/public/spot")]
    [InlineData("Mainnet", "wss://stream.bybit.com/v5/public/spot")]
    [InlineData("Production", "wss://stream.bybit.com/v5/public/spot")]
    public void GetPublicWebSocketUrl_ShouldReturnCorrectUrl(string? env, string expectedUrl)
    {
        var url = BybitOptions.GetPublicWebSocketUrl(env, "spot");
        url.Should().Be(expectedUrl);
    }

    [Theory]
    [InlineData("Demo", "wss://stream-demo.bybit.com/v5/private")]
    [InlineData("Mainnet", "wss://stream.bybit.com/v5/private")]
    [InlineData("Production", "wss://stream.bybit.com/v5/private")]
    public void GetPrivateWebSocketUrl_ShouldReturnCorrectUrl(string? env, string expectedUrl)
    {
        var url = BybitOptions.GetPrivateWebSocketUrl(env);
        url.Should().Be(expectedUrl);
    }

    [Fact]
    public void CredentialsResolution_DemoEnvironment_ShouldReturnDemoCredentials()
    {
        var options = new BybitOptions
        {
            Environment = "Demo",
            DemoApiKey = "demo_key",
            DemoApiSecret = "demo_secret",
            MainnetApiKey = "mainnet_key",
            MainnetApiSecret = "mainnet_secret"
        };

        options.GetApiKey().Should().Be("demo_key");
        options.GetApiSecret().Should().Be("demo_secret");
    }

    [Fact]
    public void CredentialsResolution_MainnetEnvironment_ShouldReturnMainnetCredentials()
    {
        var options = new BybitOptions
        {
            Environment = "Mainnet",
            DemoApiKey = "demo_key",
            DemoApiSecret = "demo_secret",
            MainnetApiKey = "mainnet_key",
            MainnetApiSecret = "mainnet_secret"
        };

        options.GetApiKey().Should().Be("mainnet_key");
        options.GetApiSecret().Should().Be("mainnet_secret");
    }
}
