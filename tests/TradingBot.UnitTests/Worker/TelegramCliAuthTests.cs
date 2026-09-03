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
    private readonly ServiceProvider _serviceProvider;

    public TelegramCliAuthTests()
    {
        _mockQrAuthService = new Mock<ITelegramQrAuthService>();
        _mockAuthService = new Mock<ITelegramAuthenticationService>();

        var services = new ServiceCollection();
        services.AddSingleton(_mockQrAuthService.Object);
        services.AddSingleton(_mockAuthService.Object);
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
                Account = new TelegramAccountDto { FirstName = "John", LastName = "Doe", Username = "johndoe", Phone = "+123456789" }
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
        _mockQrAuthService
            .Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramStatusDto { Connected = false });

        _mockAuthService
            .Setup(x => x.StartOtpLoginAsync("+1234567890", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpStartResult { Success = true, PhoneCodeHash = "hash123" });

        _mockAuthService
            .Setup(x => x.VerifyOtpAsync("+1234567890", "hash123", "12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult { Success = true });

        using var inputReader = new StringReader("1\n+1234567890\n12345\n");
        using var outputWriter = new StringWriter();

        // Act
        await TelegramCliAuth.RunAsync(_serviceProvider, inputReader, outputWriter);

        // Assert
        var output = outputWriter.ToString();
        Assert.Contains("Select Telegram Login Method:", output);
        Assert.Contains("Telegram authenticated successfully!", output);
        _mockAuthService.Verify(x => x.StartOtpLoginAsync("+1234567890", It.IsAny<CancellationToken>()), Times.Once);
        _mockAuthService.Verify(x => x.VerifyOtpAsync("+1234567890", "hash123", "12345", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_OtpFlow_Requires2FAPassword()
    {
        // Arrange
        _mockQrAuthService
            .Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramStatusDto { Connected = false });

        _mockAuthService
            .Setup(x => x.StartOtpLoginAsync("+1234567890", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpStartResult { Success = true, PhoneCodeHash = "hash123" });

        _mockAuthService
            .Setup(x => x.VerifyOtpAsync("+1234567890", "hash123", "12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult { Success = false, RequiresPassword = true });

        _mockAuthService
            .Setup(x => x.VerifyPasswordAsync("mySecret2FA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResult { Success = true });

        using var inputReader = new StringReader("1\n+1234567890\n12345\nmySecret2FA\n");
        using var outputWriter = new StringWriter();

        // Act
        await TelegramCliAuth.RunAsync(_serviceProvider, inputReader, outputWriter);

        // Assert
        var output = outputWriter.ToString();
        Assert.Contains("Two-Factor Authentication (2FA) Password Required!", output);
        Assert.Contains("Telegram 2FA authentication successful!", output);
        _mockAuthService.Verify(x => x.VerifyPasswordAsync("mySecret2FA", It.IsAny<CancellationToken>()), Times.Once);
    }
}
