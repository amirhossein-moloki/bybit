using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Telegram;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class NotificationEngineTests
{
    private readonly Mock<IEventSanitizer> _mockSanitizer;
    private readonly TelegramMessageBuilder _messageBuilder;

    public NotificationEngineTests()
    {
        _mockSanitizer = new Mock<IEventSanitizer>();
        _mockSanitizer.Setup(s => s.Sanitize(It.IsAny<string>())).Returns<string>(s => s);
        _messageBuilder = new TelegramMessageBuilder(_mockSanitizer.Object);
    }

    [Fact]
    public void Notification_ShouldSupportStateTransitions_ExactlyAsRequired()
    {
        // Arrange
        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "Telegram",
            recipient: "-1234567890",
            title: "Started",
            message: "Hello"
        );

        // Assert initial state
        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.AttemptCount.Should().Be(0);

        // Transition: Pending -> Processing
        notification.MarkProcessing();
        notification.Status.Should().Be(NotificationStatus.Processing);
        notification.AttemptCount.Should().Be(1);
        notification.LastAttemptAt.Should().NotBeNull();

        // Transition: Processing -> RetryScheduled
        var retryTime = DateTime.UtcNow.AddMinutes(5);
        notification.ScheduleRetry(retryTime, "Transient Timeout");
        notification.Status.Should().Be(NotificationStatus.RetryScheduled);
        notification.NextAttemptAt.Should().Be(retryTime);
        notification.FailureReason.Should().Be("Transient Timeout");

        // Transition: RetryScheduled -> Processing
        notification.MarkProcessing();
        notification.Status.Should().Be(NotificationStatus.Processing);
        notification.AttemptCount.Should().Be(2);

        // Transition: Processing -> Delivered
        notification.MarkDelivered();
        notification.Status.Should().Be(NotificationStatus.Delivered);
        notification.DeliveredAt.Should().NotBeNull();
        notification.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Notification_ShouldThrowException_OnInvalidStateTransitions()
    {
        // Arrange
        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "Telegram",
            recipient: "-1234567890",
            title: "Started",
            message: "Hello"
        );

        // Invalid: Pending -> Delivered
        Action act1 = () => notification.TransitionTo(NotificationStatus.Delivered);
        act1.Should().Throw<DomainException>();

        // Pending -> Processing (Valid)
        notification.MarkProcessing();

        // Invalid: Processing -> Pending
        Action act2 = () => notification.TransitionTo(NotificationStatus.Pending);
        act2.Should().Throw<DomainException>();
    }

    [Fact]
    public void NotificationPolicy_ShouldFilterEvents_BasedOnConfigFlagsAndSeverity()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Enabled = true,
            Events = new NotificationEvents
            {
                ApplicationStarted = true,
                OrderFilled = false, // Disabled order filled notifications
                CriticalError = true
            }
        };

        var policy = new NotificationPolicy(options);

        var startedEvent = new MonitoringEvent("ApplicationStarted", "INFORMATION", "System", "Host", "Started", "Hello");
        var filledEvent = new MonitoringEvent("OrderFilled", "INFORMATION", "Bybit", "Executor", "Filled", "Filled order");
        var criticalEvent = new MonitoringEvent("DatabaseOffline", "CRITICAL", "Database", "Connection", "Offline", "DB error");

        // Act & Assert
        policy.ShouldNotify(startedEvent).Should().BeTrue();
        policy.ShouldNotify(filledEvent).Should().BeFalse(); // Disabled in options
        policy.ShouldNotify(criticalEvent).Should().BeTrue(); // Always notify critical

        // Global disable
        options.Enabled = false;
        policy.ShouldNotify(startedEvent).Should().BeFalse();
    }

    [Fact]
    public void TelegramMessageBuilder_ShouldEscapeHtml_AndSanitizeSecrets()
    {
        // Arrange
        var sanitizer = new EventSanitizer(); // Real sanitizer to test secret redaction
        var builder = new TelegramMessageBuilder(sanitizer);

        var @event = new MonitoringEvent(
            eventType: "CustomEvent",
            severity: "INFORMATION",
            source: "System & Co",
            component: "Host <Service>",
            status: "Started",
            message: "This contains an api_key=super_secret_key_123 and password: my_password_abc."
        );

        // Act
        var message = builder.BuildMessage(@event);

        // Assert HTML escaping and redaction
        message.Should().Contain("&amp;"); // & escaped to &amp;
        message.Should().NotContain("<Service>");
        message.Should().Contain("&lt;Service&gt;"); // < > escaped
        message.Should().NotContain("super_secret_key_123");
        message.Should().NotContain("my_password_abc");
        message.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void TelegramMessageBuilder_ShouldEnforceMessageSizeLimit_AndTruncateLongPayloads()
    {
        // Arrange
        var @event = new MonitoringEvent(
            eventType: "ApplicationError",
            severity: "ERROR",
            source: "System",
            component: "Logger",
            status: "Failed",
            message: new string('x', 5000) // 5000 characters
        );

        // Act
        var message = _messageBuilder.BuildMessage(@event);

        // Assert message size limit
        message.Length.Should().BeLessThanOrEqualTo(4000);
        message.Should().EndWith("... [TRUNCATED]");
    }

    [Fact]
    public void TelegramMessageBuilder_ShouldFormatSpecificTemplates_Correctly()
    {
        // Arrange
        var orderFilledEvent = new MonitoringEvent(
            eventType: "OrderFilled",
            severity: "INFORMATION",
            source: "Bybit",
            component: "Execution",
            status: "Succeeded",
            message: "Order filled.",
            orderId: Guid.NewGuid(),
            payload: "{\"Symbol\": \"BTCUSDT\", \"Side\": \"Buy\", \"ExecutedQuantity\": 0.12, \"ExecutedPrice\": 65000}"
        );

        // Act
        var message = _messageBuilder.BuildMessage(orderFilledEvent);

        // Assert properties formatting
        message.Should().Contain("Order Filled");
        message.Should().Contain("Symbol: BTCUSDT");
        message.Should().Contain("Side: Buy");
        message.Should().Contain("Quantity: 0.12");
        message.Should().Contain("Price: 65000");
    }

    [Fact]
    public async Task TelegramNotificationChannel_ShouldClassifyPermanentErrors_AsNonRetryable()
    {
        // Arrange
        var mockClient = new Mock<ITelegramClient>();
        mockClient.Setup(c => c.IsConnected()).Returns(true);

        // Setup mock to throw an exception resembling TL.RpcException or simply an exception with permanent keywords
        mockClient.Setup(c => c.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("CHAT_ID_INVALID: The chat ID is invalid."));

        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { Enabled = true });
        var channel = new TelegramNotificationChannel(mockClient.Object, options, Mock.Of<ILogger<TelegramNotificationChannel>>());

        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "Telegram",
            recipient: "987654321", // valid number format
            title: "Title",
            message: "Msg"
        );

        // Act
        var result = await channel.SendAsync(notification);

        // Assert permanent error handling
        result.Success.Should().BeFalse();
        result.IsRetryable.Should().BeFalse(); // Permanent, should NOT retry
        result.ErrorCode.Should().Be("PERMANENT_TELEGRAM_ERROR");
    }

    [Fact]
    public async Task TelegramNotificationChannel_ShouldClassifyTransientErrors_AsRetryable()
    {
        // Arrange
        var mockClient = new Mock<ITelegramClient>();
        mockClient.Setup(c => c.IsConnected()).Returns(true);
        mockClient.Setup(c => c.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>()))
            .ThrowsAsync(new TimeoutException("Operation timed out."));

        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { Enabled = true });
        var channel = new TelegramNotificationChannel(mockClient.Object, options, Mock.Of<ILogger<TelegramNotificationChannel>>());

        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "Telegram",
            recipient: "987654321",
            title: "Title",
            message: "Msg"
        );

        // Act
        var result = await channel.SendAsync(notification);

        // Assert transient error handling
        result.Success.Should().BeFalse();
        result.IsRetryable.Should().BeTrue(); // Timeout, should retry!
        result.ErrorCode.Should().Be("TIMEOUT");
    }
}
