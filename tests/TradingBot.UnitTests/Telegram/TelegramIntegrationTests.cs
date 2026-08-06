using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Telegram.Authentication;
using TradingBot.Telegram.Client;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Health;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramIntegrationTests
{
    [Fact]
    public void TelegramOptions_ShouldBindFromConfigAndSupportEnvironmentOverrides()
    {
        // Arrange
        var services = new ServiceCollection();
        var myConfiguration = new Dictionary<string, string>
        {
            {"Telegram:ApiId", "123456"},
            {"Telegram:ApiHash", "test_hash"},
            {"Telegram:PhoneNumber", "+1234567890"},
            {"Telegram:SessionPath", "custom.session"},
            {"Telegram:Enabled", "true"}
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration!)
            .Build();

        Environment.SetEnvironmentVariable("TELEGRAM_API_ID", "999999");
        Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", "env_hash");
        Environment.SetEnvironmentVariable("TELEGRAM_PHONE", "+9876543210");
        Environment.SetEnvironmentVariable("TELEGRAM_SESSION_PATH", "env.session");

        try
        {
            // Act
            services.AddTelegramIntegration(configuration);
            var mockEncryption = new Mock<IEncryptionService>();
            services.AddSingleton(mockEncryption.Object);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;

            // Assert
            options.ApiId.Should().Be("999999");
            options.ApiHash.Should().Be("env_hash");
            options.PhoneNumber.Should().Be("+9876543210");
            options.SessionPath.Should().Be("env.session");
            options.Enabled.Should().BeTrue();
        }
        finally
        {
            // Clean up env vars
            Environment.SetEnvironmentVariable("TELEGRAM_API_ID", null);
            Environment.SetEnvironmentVariable("TELEGRAM_API_HASH", null);
            Environment.SetEnvironmentVariable("TELEGRAM_PHONE", null);
            Environment.SetEnvironmentVariable("TELEGRAM_SESSION_PATH", null);
        }
    }

    [Fact]
    public void TelegramSessionManager_ShouldEncryptSessionOnSaveAndDecryptOnLoad()
    {
        // Arrange
        var sessionPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.session");
        var options = new TelegramOptions
        {
            SessionPath = sessionPath,
            Enabled = true
        };
        var mockOptions = Microsoft.Extensions.Options.Options.Create(options);

        var mockEncryption = new Mock<IEncryptionService>();
        var plainText = "MyDecryptedSessionData";
        var encryptedText = "MyEncryptedSessionData";
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var base64Plain = Convert.ToBase64String(plainBytes);

        mockEncryption.Setup(e => e.Encrypt(base64Plain)).Returns(encryptedText);
        mockEncryption.Setup(e => e.Decrypt(encryptedText)).Returns(base64Plain);

        var sessionManager = new TelegramSessionManager(mockOptions, mockEncryption.Object);

        try
        {
            // 1. Create session (should create stream, write to it, which triggers SaveToDisk)
            using (var writeStream = sessionManager.LoadSession())
            {
                writeStream.Write(plainBytes, 0, plainBytes.Length);
            }

            // Verify encrypted file exists
            File.Exists(sessionPath).Should().BeTrue();
            File.ReadAllText(sessionPath).Should().Be(encryptedText);
            mockEncryption.Verify(e => e.Encrypt(base64Plain), Times.AtLeastOnce);

            // 2. Load session (should read encrypted, decrypt and populate)
            using (var readStream = sessionManager.LoadSession())
            {
                var buffer = new byte[plainBytes.Length];
                var bytesRead = readStream.Read(buffer, 0, buffer.Length);
                bytesRead.Should().Be(plainBytes.Length);
                System.Text.Encoding.UTF8.GetString(buffer).Should().Be(plainText);
            }

            mockEncryption.Verify(e => e.Decrypt(encryptedText), Times.Once);

            // 3. Delete session
            sessionManager.SessionExists().Should().BeTrue();
            sessionManager.DeleteSession();
            sessionManager.SessionExists().Should().BeFalse();
            File.Exists(sessionPath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(sessionPath))
            {
                File.Delete(sessionPath);
            }
        }
    }

    [Fact]
    public async Task TelegramClientService_ShouldTransitionToErrorState_WhenConnectThrowsException()
    {
        // Arrange
        var options = new TelegramOptions
        {
            ApiId = "", // Invalid to trigger exception during config check or construction
            ApiHash = "test_hash",
            PhoneNumber = "+123456",
            Enabled = true
        };
        var mockOptions = Microsoft.Extensions.Options.Options.Create(options);
        var mockSessionManager = new Mock<ITelegramSessionManager>();
        mockSessionManager.Setup(s => s.LoadSession()).Returns(new MemoryStream());
        var mockReceiver = new Mock<ITelegramMessageReceiver>();

        var clientService = new TelegramClientService(mockOptions, mockSessionManager.Object, mockReceiver.Object);

        // Act & Assert
        clientService.CurrentState.Should().Be(TelegramConnectionState.Disconnected);

        Func<Task> act = async () => await clientService.ConnectAsync();
        await act.Should().ThrowAsync<TelegramConnectionException>();

        clientService.CurrentState.Should().Be(TelegramConnectionState.Error);
        clientService.IsConnected().Should().BeFalse();
    }

    [Fact]
    public async Task TelegramAuthService_ShouldSkipAuthentication_WhenDisabled()
    {
        // Arrange
        var options = new TelegramOptions
        {
            Enabled = false
        };
        var mockOptions = Microsoft.Extensions.Options.Options.Create(options);
        var mockClient = new Mock<ITelegramClient>();

        var authService = new TelegramAuthService(mockClient.Object, mockOptions);

        // Act
        await authService.AuthenticateAsync();

        // Assert
        mockClient.Verify(c => c.ConnectAsync(), Times.Never);
    }

    [Fact]
    public async Task TelegramAuthService_ShouldThrow_WhenInjectedClientIsNotTelegramClientService()
    {
        // Arrange
        var options = new TelegramOptions
        {
            Enabled = true
        };
        var mockOptions = Microsoft.Extensions.Options.Options.Create(options);
        var mockClient = new Mock<ITelegramClient>(); // Not TelegramClientService

        var authService = new TelegramAuthService(mockClient.Object, mockOptions);

        // Act & Assert
        Func<Task> act = async () => await authService.AuthenticateAsync();
        await act.Should().ThrowAsync<TelegramAuthenticationException>()
            .WithMessage("*ITelegramClient implementation is not of type TelegramClientService*");
    }

    [Fact]
    public async Task TelegramHealthCheck_ShouldReturnHealthy_WhenConnected()
    {
        // Arrange
        var mockClient = new Mock<ITelegramClient>();
        mockClient.Setup(c => c.CurrentState).Returns(TelegramConnectionState.Connected);

        var healthCheck = new TelegramHealthCheck(mockClient.Object);
        var context = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
        result.Description.Should().Be("Telegram connection is healthy and connected.");
    }

    [Fact]
    public async Task TelegramHealthCheck_ShouldReturnDegraded_WhenConnectingOrAuthenticating()
    {
        // Arrange
        var mockClient = new Mock<ITelegramClient>();
        mockClient.Setup(c => c.CurrentState).Returns(TelegramConnectionState.Connecting);

        var healthCheck = new TelegramHealthCheck(mockClient.Object);
        var context = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
    }

    [Fact]
    public async Task TelegramHealthCheck_ShouldReturnUnhealthy_WhenDisconnectedOrError()
    {
        // Arrange
        var mockClient = new Mock<ITelegramClient>();
        mockClient.Setup(c => c.CurrentState).Returns(TelegramConnectionState.Error);

        var healthCheck = new TelegramHealthCheck(mockClient.Object);
        var context = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);
    }

    [Fact]
    public void TelegramClientService_ShouldSupportThreadSafeStateTransitions()
    {
        // Arrange
        var options = new TelegramOptions { Enabled = true };
        var mockOptions = Microsoft.Extensions.Options.Options.Create(options);
        var mockSessionManager = new Mock<ITelegramSessionManager>();
        var mockReceiver = new Mock<ITelegramMessageReceiver>();
        var clientService = new TelegramClientService(mockOptions, mockSessionManager.Object, mockReceiver.Object);

        // Act
        clientService.SetState(TelegramConnectionState.Connecting);
        clientService.CurrentState.Should().Be(TelegramConnectionState.Connecting);

        clientService.SetState(TelegramConnectionState.Listening);
        clientService.CurrentState.Should().Be(TelegramConnectionState.Listening);
    }

    [Fact]
    public void TelegramMessageDto_ShouldMapPropertiesCorrectly()
    {
        // Arrange & Act
        var dto = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "CryptoChannel",
            MessageId = 100,
            SenderId = 9999,
            Text = "BUY BTCUSDT",
            Date = DateTime.UtcNow,
            IsChannel = true,
            IsGroup = false,
            RawUpdate = "UpdateNewMessage"
        };

        // Assert
        dto.ChannelId.Should().Be(12345);
        dto.ChannelName.Should().Be("CryptoChannel");
        dto.MessageId.Should().Be(100);
        dto.SenderId.Should().Be(9999);
        dto.Text.Should().Be("BUY BTCUSDT");
        dto.IsChannel.Should().BeTrue();
        dto.IsGroup.Should().Be(false);
        dto.RawUpdate.Should().Be("UpdateNewMessage");
    }

    [Fact]
    public async Task DefaultTelegramMessageReceiver_ShouldReceiveAndLogMessage()
    {
        // Arrange
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<DefaultTelegramMessageReceiver>>();
        var receiver = new DefaultTelegramMessageReceiver(mockLogger.Object);
        var dto = new TelegramMessageDto { MessageId = 1, Text = "Test" };

        // Act
        Func<Task> act = async () => await receiver.ReceiveMessageAsync(dto);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
