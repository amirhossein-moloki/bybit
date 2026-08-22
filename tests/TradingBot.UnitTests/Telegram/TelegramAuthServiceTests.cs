using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using TL;
using TradingBot.Telegram.Authentication;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramAuthServiceTests
{
    private readonly Mock<ITelegramClient> _mockClient;
    private readonly Mock<ITelegramSessionManager> _mockSessionManager;
    private readonly TelegramOptions _options;
    private readonly TelegramAuthService _authService;

    public TelegramAuthServiceTests()
    {
        _mockClient = new Mock<ITelegramClient>();
        _mockSessionManager = new Mock<ITelegramSessionManager>();
        _options = new TelegramOptions
        {
            Enabled = true,
            ApiId = "12345",
            ApiHash = "test_hash",
            PhoneNumber = "+1234567890"
        };
        var optionsMock = Microsoft.Extensions.Options.Options.Create(_options);

        _authService = new TelegramAuthService(_mockClient.Object, _mockSessionManager.Object, optionsMock);
    }

    [Fact]
    public async Task StartOtpLoginAsync_ShouldReturnError_WhenPhoneNumberIsEmpty()
    {
        // Act
        var result = await _authService.StartOtpLoginAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Phone number is required.");
    }

    [Fact]
    public async Task VerifyOtpAsync_ShouldReturnError_WhenCodeIsEmpty()
    {
        // Act
        var result = await _authService.VerifyOtpAsync("+1234567890", "hash", "");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Phone number and code are required.");
    }

    [Fact]
    public async Task VerifyPasswordAsync_ShouldReturnError_WhenPasswordIsEmpty()
    {
        // Act
        var result = await _authService.VerifyPasswordAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Password is required.");
    }

    [Fact]
    public async Task IsAuthenticatedAsync_ShouldReturnFalse_WhenDisabled()
    {
        // Arrange
        _options.Enabled = false;

        // Act
        var result = await _authService.IsAuthenticatedAsync();

        // Assert
        result.Should().BeFalse();
    }
}
