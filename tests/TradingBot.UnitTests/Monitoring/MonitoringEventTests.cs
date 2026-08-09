using System;
using FluentAssertions;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Exceptions;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class MonitoringEventTests
{
    [Fact]
    public void Constructor_ShouldInitializeCorrectly_WhenAllRequiredFieldsProvided()
    {
        // Arrange
        var id = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        // Act
        var @event = new MonitoringEvent(
            eventType: "SignalReceived",
            severity: "INFORMATION",
            source: "Telegram",
            component: "TelegramReceiver",
            status: "Detected",
            message: "Signal received from channel",
            correlationId: "corr-123",
            operationId: "op-456",
            signalId: signalId,
            orderId: orderId,
            positionId: positionId,
            payload: "{\"test\": \"payload\"}",
            errorCode: "ERR_CODE",
            exceptionType: "System.Exception",
            externalEventId: "ext-789",
            timestamp: timestamp
        );

        // Assert
        @event.Id.Should().NotBeEmpty();
        @event.EventType.Should().Be("SignalReceived");
        @event.Severity.Should().Be("INFORMATION");
        @event.Source.Should().Be("Telegram");
        @event.Component.Should().Be("TelegramReceiver");
        @event.Status.Should().Be("Detected");
        @event.Message.Should().Be("Signal received from channel");
        @event.CorrelationId.Should().Be("corr-123");
        @event.OperationId.Should().Be("op-456");
        @event.SignalId.Should().Be(signalId);
        @event.OrderId.Should().Be(orderId);
        @event.PositionId.Should().Be(positionId);
        @event.Payload.Should().Be("{\"test\": \"payload\"}");
        @event.ErrorCode.Should().Be("ERR_CODE");
        @event.ExceptionType.Should().Be("System.Exception");
        @event.ExternalEventId.Should().Be("ext-789");
        @event.Timestamp.Should().Be(timestamp);
    }

    [Theory]
    [InlineData("", "INFO", "Src", "Comp", "Status", "Msg")]
    [InlineData("Type", "", "Src", "Comp", "Status", "Msg")]
    [InlineData("Type", "INFO", "", "Comp", "Status", "Msg")]
    [InlineData("Type", "INFO", "Src", "", "Status", "Msg")]
    [InlineData("Type", "INFO", "Src", "Comp", "", "Msg")]
    [InlineData("Type", "INFO", "Src", "Comp", "Status", "")]
    public void Constructor_ShouldThrowDomainException_WhenRequiredFieldIsEmpty(
        string eventType, string severity, string source, string component, string status, string message)
    {
        // Act & Assert
        Action act = () => new MonitoringEvent(eventType, severity, source, component, status, message);
        act.Should().Throw<DomainException>();
    }
}
