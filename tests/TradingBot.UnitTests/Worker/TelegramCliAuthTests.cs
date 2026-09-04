using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Worker;
using Xunit;

namespace TradingBot.UnitTests.Worker;

public class TelegramCliAuthTests
{
    private readonly Mock<ITelegramQrAuthService> _mockQrAuthService;
    private readonly Mock<ITelegramAuthenticationService> _mockAuthService;
    private readonly Mock<ITelegramClient> _mockClient;
    private readonly ServiceProvider _serviceProvider;

    public TelegramCliAuthTests()
    {
        _mockQrAuthService = new Mock<ITelegramQrAuthService>();
        _mockAuthService = new Mock<ITelegramAuthenticationService>();
        _mockClient = new Mock<ITelegramClient>();

        var services = new ServiceCollection();
        services.AddSingleton(_mockQrAuthService.Object);
        services.AddSingleton(_mockAuthService.Object);
        services.AddSingleton(_mockClient.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyConnectedAndUserChoosesNotToReauth_ExitsEarly()
    {
        // Arrange
        _mockQrAuthService
            .Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramStatusDto
            {
                Connected = true,
                Account = new TelegramAccountDto { FirstName = "John", LastName = "Doe", Username = "johndoe", Phone = "+15550100000" }
            });

        using var inputReader = new StringReader("n\n");
        using var outputWriter = new StringWriter();

        // Act
        await TelegramCliAuth.RunAsync(_serviceProvider, inputReader, outputWriter);

        // Assert
        var output = outputWriter.ToString();
        Assert.Contains("Telegram is ALREADY CONNECTED as: John Doe (@johndoe)", output);
        Assert.Contains("Exiting Telegram CLI authentication tool.", output);
        _mockQrAuthService.Verify(x => x.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_OtpFlow_SuccessfulAuthentication()
    {
        // Arrange
        const string mockPhone = "+15550100000";
        const string mockHash = "token_hash";
        const string mockCode = "99999";

        _mockQrAuthService
            .Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramStatusDto { Connected = false });

        _mockAuthService
            .Setup(x => x.StartOtpLoginAsync(mockPhone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpStartResult { Success = true, PhoneCodeHash = mockHash });

        _mockAuthService
            .Setup(x => x.VerifyOtpAsync(mockPhone, mockHash, mockCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult { Success = true });

        using var inputReader = new StringReader($"1\n{mockPhone}\n{mockCode}\n");
        using var outputWriter = new StringWriter();

        // Act
        await TelegramCliAuth.RunAsync(_serviceProvider, inputReader, outputWriter);

        // Assert
        var output = outputWriter.ToString();
        Assert.Contains("Select Telegram Login Method:", output);
        Assert.Contains("Telegram authentication completed successfully.", output);
        _mockAuthService.Verify(x => x.StartOtpLoginAsync(mockPhone, It.IsAny<CancellationToken>()), Times.Once);
        _mockAuthService.Verify(x => x.VerifyOtpAsync(mockPhone, mockHash, mockCode, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_OtpFlow_Requires2FAPassword()
    {
        // Arrange
        const string mockPhone = "+15550100000";
        const string mockHash = "token_hash";
        const string mockCode = "99999";
        const string mockInputPassword = "sample_code";

        var clientService = new TradingBot.Telegram.Client.TelegramClientService(
            Microsoft.Extensions.Options.Options.Create(new TradingBot.Telegram.Configuration.TelegramOptions { Enabled = true, ApiId = "12345", ApiHash = "hash", PhoneNumber = mockPhone }),
            Mock.Of<ITelegramSessionManager>(),
            Mock.Of<ITelegramMessageReceiver>()
        );

        var services = new ServiceCollection();
        services.AddSingleton(_mockQrAuthService.Object);
        services.AddSingleton(_mockAuthService.Object);
        services.AddSingleton<ITelegramClient>(clientService);
        var customServiceProvider = services.BuildServiceProvider();

        _mockQrAuthService
            .Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramStatusDto { Connected = false });

        _mockAuthService
            .Setup(x => x.StartOtpLoginAsync(mockPhone, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpStartResult { Success = true, PhoneCodeHash = mockHash });

        _mockAuthService
            .Setup(x => x.VerifyOtpAsync(mockPhone, mockHash, mockCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult { Success = false, RequiresPassword = true });

        _mockAuthService
            .Setup(x => x.VerifyPasswordAsync(mockInputPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResult { Success = true });

        using var inputReader = new StringReader($"1\n{mockPhone}\n{mockCode}\n{mockInputPassword}\n");
        using var outputWriter = new StringWriter();

        // Act
        await TelegramCliAuth.RunAsync(customServiceProvider, inputReader, outputWriter);

        // Assert
        var output = outputWriter.ToString();
        Assert.Contains("Telegram authentication completed successfully.", output);
        _mockAuthService.Verify(x => x.VerifyPasswordAsync(mockInputPassword, It.IsAny<CancellationToken>()), Times.Once);
    }
}
