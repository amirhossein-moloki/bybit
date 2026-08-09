using System;
using System.Threading.Tasks;
using FluentAssertions;
using TradingBot.Application.Monitoring.Services;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class MetricsServiceTests
{
    [Fact]
    public void GetUptime_ShouldReturnPositiveTimeSpan()
    {
        // Arrange
        var service = new MetricsService();

        // Act
        var uptime = service.GetUptime();

        // Assert
        uptime.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Increments_ShouldRegisterInAggregatedMetrics()
    {
        // Arrange
        var service = new MetricsService();

        // Act
        service.IncrementAlertsTriggered();
        service.IncrementAlertsTriggered();
        service.IncrementAlertsResolved();
        service.IncrementAlertsDeduplicated();
        service.IncrementNotificationsSuppressed();
        service.IncrementNotificationsCreated();
        service.IncrementNotificationsDelivered();
        service.IncrementNotificationsFailed();
        service.IncrementNotificationsRetried();

        service.IncrementSystemErrors();
        service.IncrementSystemWarnings();
        service.IncrementSystemCriticalErrors();

        service.IncrementSignalsReceived();
        service.IncrementSignalsAccepted();
        service.IncrementSignalsRejected();

        service.IncrementOrdersSubmitted();
        service.IncrementOrdersFilled();
        service.IncrementOrdersFailed();
        service.IncrementOrdersRejected();
        service.IncrementOrdersCancelled();

        service.IncrementPositionsOpened();
        service.IncrementPositionsClosed();

        service.IncrementTelegramMessagesReceived();
        service.IncrementTelegramMessagesProcessed();
        service.IncrementTelegramMessagesFailed();

        // Assert
        var metrics = service.GetAggregatedMetrics();

        metrics["AlertsTriggered"].Should().Be(2L);
        metrics["AlertsResolved"].Should().Be(1L);
        metrics["AlertsDeduplicated"].Should().Be(1L);
        metrics["NotificationsSuppressed"].Should().Be(1L);
        metrics["NotificationsCreated"].Should().Be(1L);
        metrics["NotificationsDelivered"].Should().Be(1L);
        metrics["NotificationsFailed"].Should().Be(1L);
        metrics["NotificationsRetried"].Should().Be(1L);

        metrics["SystemErrors"].Should().Be(1L);
        metrics["SystemWarnings"].Should().Be(1L);
        metrics["SystemCriticalErrors"].Should().Be(1L);

        metrics["SignalsReceived"].Should().Be(1L);
        metrics["SignalsAccepted"].Should().Be(1L);
        metrics["SignalsRejected"].Should().Be(1L);

        metrics["OrdersSubmitted"].Should().Be(1L);
        metrics["OrdersFilled"].Should().Be(1L);
        metrics["OrdersFailed"].Should().Be(1L);
        metrics["OrdersRejected"].Should().Be(1L);
        metrics["OrdersCancelled"].Should().Be(1L);

        metrics["PositionsOpened"].Should().Be(1L);
        metrics["PositionsClosed"].Should().Be(1L);

        metrics["TelegramMessagesReceived"].Should().Be(1L);
        metrics["TelegramMessagesProcessed"].Should().Be(1L);
        metrics["TelegramMessagesFailed"].Should().Be(1L);
    }

    [Fact]
    public void RecordApiCall_ShouldCalculateLatencyMetricsCorrectly()
    {
        // Arrange
        var service = new MetricsService();

        // Act
        service.RecordApiCall("BybitGET", 100, true, false, false);
        service.RecordApiCall("BybitGET", 300, true, false, false);
        service.RecordApiCall("BybitGET", 200, false, true, true);

        // Assert
        var metrics = service.GetAggregatedMetrics();
        var apis = (System.Collections.Generic.Dictionary<string, object>)metrics["ApiMetrics"];

        apis.Should().ContainKey("BybitGET");
        var val = apis["BybitGET"];

        val.GetType().GetProperty("RequestCount")?.GetValue(val).Should().Be(3L);
        val.GetType().GetProperty("SuccessCount")?.GetValue(val).Should().Be(2L);
        val.GetType().GetProperty("FailureCount")?.GetValue(val).Should().Be(1L);
        val.GetType().GetProperty("TimeoutCount")?.GetValue(val).Should().Be(1L);
        val.GetType().GetProperty("RateLimitCount")?.GetValue(val).Should().Be(1L);

        val.GetType().GetProperty("MinLatencyMs")?.GetValue(val).Should().Be(100.0);
        val.GetType().GetProperty("MaxLatencyMs")?.GetValue(val).Should().Be(300.0);
        val.GetType().GetProperty("AvgLatencyMs")?.GetValue(val).Should().Be(200.0);
    }

    [Fact]
    public void RecordLatency_ShouldTrackPathsCorrectly()
    {
        // Arrange
        var service = new MetricsService();

        // Act
        service.RecordLatency("Signal Processing", 50);
        service.RecordLatency("Signal Processing", 150);

        // Assert
        var metrics = service.GetAggregatedMetrics();
        var paths = (System.Collections.Generic.Dictionary<string, object>)metrics["LatencyPaths"];

        paths.Should().ContainKey("Signal Processing");
        var val = paths["Signal Processing"];

        val.GetType().GetProperty("Count")?.GetValue(val).Should().Be(2L);
        val.GetType().GetProperty("MinLatencyMs")?.GetValue(val).Should().Be(50.0);
        val.GetType().GetProperty("MaxLatencyMs")?.GetValue(val).Should().Be(150.0);
        val.GetType().GetProperty("AvgLatencyMs")?.GetValue(val).Should().Be(100.0);
    }
}
