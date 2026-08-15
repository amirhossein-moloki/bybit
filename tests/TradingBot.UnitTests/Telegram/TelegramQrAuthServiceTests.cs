using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Telegram.Authentication;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramQrAuthServiceTests
{
    private readonly Mock<ITelegramClient> _mockClient;
    private readonly Mock<ITelegramSessionManager> _mockSessionManager;
    private readonly TelegramOptions _options;
    private readonly TelegramQrAuthService _service;

    public TelegramQrAuthServiceTests()
    {
        _mockClient = new Mock<ITelegramClient>();
        _mockSessionManager = new Mock<ITelegramSessionManager>();
        _options = new TelegramOptions { Enabled = true, SessionPath = "/app/data/telegram/session" };
        var mockOptions = Microsoft.Extensions.Options.Options.Create(_options);

        _service = new TelegramQrAuthService(_mockClient.Object, _mockSessionManager.Object, mockOptions);
    }

    [Fact]
    public async Task GetStatusAsync_ShouldReturnNotConnected_WhenSessionDoesNotExistAndClientNotConnected()
    {
        // Arrange
        _mockSessionManager.Setup(s => s.SessionExists()).Returns(false);
        _mockClient.Setup(c => c.IsConnected()).Returns(false);

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        result.Connected.Should().BeFalse();
        result.Status.Should().Be("NotConnected");
        result.Account.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ShouldReturnConnected_WhenClientIsConnectedWithAccount()
    {
        // Arrange
        _mockSessionManager.Setup(s => s.SessionExists()).Returns(true);
        _mockClient.Setup(c => c.IsConnected()).Returns(true);
        _mockClient.Setup(c => c.CurrentState).Returns(TelegramConnectionState.Listening);

        var account = new TelegramAccountDto { Id = 12345, Username = "testuser", FirstName = "Test" };
        _mockClient.Setup(c => c.GetConnectedAccount()).Returns(account);

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        result.Connected.Should().BeTrue();
        result.Status.Should().Be("Active");
        result.Account.Should().NotBeNull();
        result.Account!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task StartQrAuthAsync_ShouldReturnSessionAndQrData()
    {
        // Arrange
        _mockClient.Setup(c => c.LoginWithQrCodeAsync(It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .Callback<Action<string>, CancellationToken>((qrDisplay, ct) =>
            {
                qrDisplay("tg://login?token=test_qr_token_data");
            })
            .ReturnsAsync(new TL.User { id = 999, username = "qr_user" });

        // Act
        var result = await _service.StartQrAuthAsync();

        // Assert
        result.SessionId.Should().NotBeNullOrEmpty();
        result.QrData.Should().Be("tg://login?token=test_qr_token_data");
        result.ExpiresAt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetQrStatusAsync_ShouldReturnStatusForActiveSession()
    {
        // Arrange
        _mockClient.Setup(c => c.LoginWithQrCodeAsync(It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .Callback<Action<string>, CancellationToken>((qrDisplay, ct) =>
            {
                qrDisplay("tg://login?token=test_qr_token_data");
            })
            .ReturnsAsync(new TL.User { id = 999, username = "qr_user" });

        var startResult = await _service.StartQrAuthAsync();

        // Act
        var statusResult = await _service.GetQrStatusAsync(startResult.SessionId);

        // Assert
        statusResult.SessionId.Should().Be(startResult.SessionId);
        statusResult.QrData.Should().Be("tg://login?token=test_qr_token_data");
    }

    [Fact]
    public async Task LogoutAsync_ShouldDisconnectClientAndDeleteSession()
    {
        // Act
        await _service.LogoutAsync();

        // Assert
        _mockClient.Verify(c => c.DisconnectAsync(), Times.Once);
        _mockSessionManager.Verify(s => s.DeleteSession(), Times.Once);
        _mockClient.Verify(c => c.SetState(TelegramConnectionState.NotConnected), Times.Once);
    }
}
