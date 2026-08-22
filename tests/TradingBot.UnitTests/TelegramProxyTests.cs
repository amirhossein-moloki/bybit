using System;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TradingBot.Telegram.Client;
using TradingBot.Telegram.Configuration;
using Xunit;

namespace TradingBot.UnitTests;

public class TelegramProxyTests
{
    [Theory]
    [InlineData("socks5", "127.0.0.1", 10808, "socks_ip", "127.0.0.1")]
    [InlineData("socks5", "127.0.0.1", 10808, "socks_port", "10808")]
    [InlineData("http", "127.0.0.1", 8080, "proxy_ip", "127.0.0.1")]
    [InlineData("http", "127.0.0.1", 8080, "proxy_port", "8080")]
    public void ConfigProvider_ShouldExtractHostAndPortCorrectly(string scheme, string host, int port, string key, string expected)
    {
        var proxyUrl = $"{scheme}://{host}:{port}";
        var options = Options.Create(new TelegramOptions
        {
            ApiId = "12345",
            ApiHash = "hash123",
            PhoneNumber = "+1234567890",
            ProxyUrl = proxyUrl
        });

        var service = new TelegramClientService(options, new DummySessionManager(), new DummyMessageReceiver());
        var method = typeof(TelegramClientService).GetMethod("ConfigProvider", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = method?.Invoke(service, new object[] { key }) as string;
        result.Should().Be(expected);
    }

    [Fact]
    public void ConfigProvider_ShouldExtractSocksCredentialsCorrectly()
    {
        var user = "proxy_user";
        var pass = "proxy_secret_key";
        var proxyUrl = $"socks5://{user}:{pass}@127.0.0.1:10808";

        var options = Options.Create(new TelegramOptions
        {
            ApiId = "12345",
            ApiHash = "hash123",
            PhoneNumber = "+1234567890",
            ProxyUrl = proxyUrl
        });

        var service = new TelegramClientService(options, new DummySessionManager(), new DummyMessageReceiver());
        var method = typeof(TelegramClientService).GetMethod("ConfigProvider", BindingFlags.NonPublic | BindingFlags.Instance);

        var parsedUser = method?.Invoke(service, new object[] { "socks_username" }) as string;
        var parsedPass = method?.Invoke(service, new object[] { "socks_password" }) as string;

        parsedUser.Should().Be(user);
        parsedPass.Should().Be(pass);
    }

    [Fact]
    public void ConfigProvider_ShouldExtractHttpCredentialsCorrectly()
    {
        var user = "http_user";
        var pass = "http_secret_key";
        var proxyUrl = $"http://{user}:{pass}@127.0.0.1:8080";

        var options = Options.Create(new TelegramOptions
        {
            ApiId = "12345",
            ApiHash = "hash123",
            PhoneNumber = "+1234567890",
            ProxyUrl = proxyUrl
        });

        var service = new TelegramClientService(options, new DummySessionManager(), new DummyMessageReceiver());
        var method = typeof(TelegramClientService).GetMethod("ConfigProvider", BindingFlags.NonPublic | BindingFlags.Instance);

        var parsedUser = method?.Invoke(service, new object[] { "proxy_username" }) as string;
        var parsedPass = method?.Invoke(service, new object[] { "proxy_password" }) as string;

        parsedUser.Should().Be(user);
        parsedPass.Should().Be(pass);
    }

    [Theory]
    [InlineData("socks5://127.0.0.1:10808", "http_proxy")]
    [InlineData("socks5://127.0.0.1:10808", "proxy_ip")]
    [InlineData("http://127.0.0.1:8080", "socks_ip")]
    [InlineData("http://127.0.0.1:8080", "socks_port")]
    public void ConfigProvider_ShouldIsolateProtocolsBasedOnScheme(string proxyUrl, string keyForOtherProtocol)
    {
        var options = Options.Create(new TelegramOptions
        {
            ApiId = "12345",
            ApiHash = "hash123",
            PhoneNumber = "+1234567890",
            ProxyUrl = proxyUrl
        });

        var service = new TelegramClientService(options, new DummySessionManager(), new DummyMessageReceiver());
        var method = typeof(TelegramClientService).GetMethod("ConfigProvider", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = method?.Invoke(service, new object[] { keyForOtherProtocol }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void DependencyInjection_ShouldResolveProxyUrlFromTelegramProxyUrlEnvVar()
    {
        var envVar = "TELEGRAM_PROXY_URL";
        var proxyVal = "socks5://proxy.example.com:1080";
        Environment.SetEnvironmentVariable(envVar, proxyVal);

        try
        {
            var config = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddTelegramIntegration(config);

            var sp = services.BuildServiceProvider();
            var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;

            opts.ProxyUrl.Should().Be(proxyVal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact]
    public void DependencyInjection_ShouldResolveProxyUrlFromTelegramDoubleUnderscoreProxyUrlEnvVar()
    {
        var envVar = "Telegram__ProxyUrl";
        var proxyVal = "socks5://proxy2.example.com:1080";
        Environment.SetEnvironmentVariable(envVar, proxyVal);

        try
        {
            var config = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddTelegramIntegration(config);

            var sp = services.BuildServiceProvider();
            var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;

            opts.ProxyUrl.Should().Be(proxyVal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    private class DummySessionManager : TradingBot.Telegram.Interfaces.ITelegramSessionManager
    {
        public void DeleteSession() { }
        public System.IO.Stream LoadSession() => new System.IO.MemoryStream();
        public void SaveSession(System.IO.Stream sessionStream) { }
        public bool SessionExists() => false;
    }

    private class DummyMessageReceiver : TradingBot.Telegram.Interfaces.ITelegramMessageReceiver
    {
        public System.Threading.Tasks.Task ReceiveMessageAsync(TradingBot.Telegram.Models.TelegramMessageDto message) => System.Threading.Tasks.Task.CompletedTask;
    }
}
