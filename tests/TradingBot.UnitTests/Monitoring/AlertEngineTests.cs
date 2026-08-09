using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class AlertEngineTests
{
    private readonly Mock<IAlertRepository> _alertRepoMock;
    private readonly Mock<IAlertEventRepository> _alertEventRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<INotificationEngine> _notificationEngineMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<ILogger<AlertEngine>> _loggerMock;
    private readonly IServiceProvider _serviceProvider;

    public AlertEngineTests()
    {
        _alertRepoMock = new Mock<IAlertRepository>();
        _alertEventRepoMock = new Mock<IAlertEventRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _notificationEngineMock = new Mock<INotificationEngine>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _loggerMock = new Mock<ILogger<AlertEngine>>();

        var services = new ServiceCollection();
        services.AddSingleton(_alertRepoMock.Object);
        services.AddSingleton(_alertEventRepoMock.Object);
        services.AddSingleton(_unitOfWorkMock.Object);
        services.AddSingleton(_notificationEngineMock.Object);
        services.AddSingleton(_metricsServiceMock.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProcessEventAsync_ShouldIgnore_WhenAlertingDisabled()
    {
        // Arrange
        var options = new AlertOptions { Enabled = false };
        var engine = new AlertEngine(_serviceProvider, options, _loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        // Act
        await engine.ProcessEventAsync(@event);

        // Assert
        _alertRepoMock.Verify(x => x.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessEventAsync_ShouldTriggerImmediateAlert_WhenRuleMatchesNoThreshold()
    {
        // Arrange
        var options = new AlertOptions
        {
            Enabled = true,
            Rules = new Dictionary<string, AlertRuleSettings>
            {
                ["BybitDisconnected"] = new()
                {
                    Enabled = true,
                    Severity = "WARNING",
                    EventType = "BybitDisconnected",
                    Component = "Bybit"
                }
            }
        };

        var engine = new AlertEngine(_serviceProvider, options, _loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        _alertRepoMock.Setup(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Alert)null!);

        // Act
        await engine.ProcessEventAsync(@event);

        // Assert
        _alertRepoMock.Verify(x => x.AddAsync(It.Is<Alert>(a =>
            a.RuleId == "BybitDisconnected" &&
            a.Status == "Triggered" &&
            a.Severity == "WARNING" &&
            a.Component == "Bybit"
        ), It.IsAny<CancellationToken>()), Times.Once);

        _notificationEngineMock.Verify(x => x.ProcessEventAsync(It.Is<MonitoringEvent>(e =>
            e.EventType == "AlertEvent" &&
            e.Severity == "WARNING" &&
            e.Message.Contains("Alert Triggered")
        ), It.IsAny<CancellationToken>()), Times.Once);

        _metricsServiceMock.Verify(x => x.IncrementAlertsTriggered(), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_ShouldCreateInactiveAlert_WhenRuleHasThreshold()
    {
        // Arrange
        var options = new AlertOptions
        {
            Enabled = true,
            Rules = new Dictionary<string, AlertRuleSettings>
            {
                ["BybitDisconnected"] = new()
                {
                    Enabled = true,
                    Severity = "WARNING",
                    Threshold = "30s"
                }
            }
        };

        var engine = new AlertEngine(_serviceProvider, options, _loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        _alertRepoMock.Setup(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Alert)null!);

        // Act
        await engine.ProcessEventAsync(@event);

        // Assert
        _alertRepoMock.Verify(x => x.AddAsync(It.Is<Alert>(a =>
            a.RuleId == "BybitDisconnected" &&
            a.Status == "Inactive"
        ), It.IsAny<CancellationToken>()), Times.Once);

        _notificationEngineMock.Verify(x => x.ProcessEventAsync(It.IsAny<MonitoringEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _metricsServiceMock.Verify(x => x.IncrementAlertsTriggered(), Times.Never);
    }

    [Fact]
    public async Task ProcessEventAsync_ShouldDeduplicate_WhenActiveAlertExists()
    {
        // Arrange
        var options = new AlertOptions
        {
            Enabled = true,
            Rules = new Dictionary<string, AlertRuleSettings>
            {
                ["BybitDisconnected"] = new() { Enabled = true, Severity = "WARNING" }
            }
        };

        var engine = new AlertEngine(_serviceProvider, options, _loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        var existingAlert = new Alert(
            ruleId: "BybitDisconnected",
            alertType: "BybitDisconnected",
            severity: "WARNING",
            status: "Triggered",
            source: "Exchange",
            component: "Bybit",
            message: "Bybit connection lost.",
            deduplicationKey: "BybitDisconnected:Bybit:BybitDisconnected"
        );

        _alertRepoMock.Setup(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAlert);

        // Act
        await engine.ProcessEventAsync(@event);

        // Assert: TriggerCount incremented, but no new Alert added
        existingAlert.TriggerCount.Should().Be(2);
        _alertRepoMock.Verify(x => x.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
        _alertRepoMock.Verify(x => x.Update(existingAlert), Times.AtLeastOnce);
        _metricsServiceMock.Verify(x => x.IncrementAlertsDeduplicated(), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_ShouldResolveActiveAlerts_WhenRecoveryEventReceived()
    {
        // Arrange
        var options = new AlertOptions { Enabled = true };
        var engine = new AlertEngine(_serviceProvider, options, _loggerMock.Object);

        var recoveryEvent = new MonitoringEvent(
            eventType: "BybitConnectionRestored",
            severity: "INFORMATION",
            source: "Exchange",
            component: "Bybit",
            status: "Connected",
            message: "Bybit connection restored."
        );

        var activeAlert = new Alert(
            ruleId: "BybitDisconnected",
            alertType: "BybitDisconnected",
            severity: "WARNING",
            status: "Triggered",
            source: "Exchange",
            component: "Bybit",
            message: "Bybit connection lost.",
            deduplicationKey: "BybitDisconnected:Bybit:BybitDisconnected"
        );

        _alertRepoMock.Setup(x => x.GetActiveAlertsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert> { activeAlert });

        // Act
        await engine.ProcessEventAsync(recoveryEvent);

        // Assert
        activeAlert.Status.Should().Be("Resolved");
        activeAlert.ResolvedAt.Should().NotBeNull();
        _alertRepoMock.Verify(x => x.Update(activeAlert), Times.Once);
        _alertEventRepoMock.Verify(x => x.AddAsync(It.Is<AlertEvent>(e => e.NewStatus == "Resolved"), It.IsAny<CancellationToken>()), Times.Once);
        _metricsServiceMock.Verify(x => x.IncrementAlertsResolved(), Times.Once);

        _notificationEngineMock.Verify(x => x.ProcessEventAsync(It.Is<MonitoringEvent>(e =>
            e.EventType == "AlertEvent" &&
            e.Message.Contains("Alert Resolved")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
