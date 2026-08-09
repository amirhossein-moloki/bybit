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
using TradingBot.Domain.Enums;
using TradingBot.Worker;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class MonitoringTransitionTests
{
    private readonly Mock<IHealthStatusProvider> _healthProviderMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<MonitoringWorker>> _loggerMock = new();
    private readonly MonitoringOptions _options = new();

    [Fact]
    public async Task MonitoringWorker_ShouldEmitHealthStatusChanged_WhenStatusTransitions()
    {
        // Arrange
        var healthCheckEngineMock = new Mock<IHealthCheckEngine>();
        var resultRepoMock = new Mock<IHealthCheckResultRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var publisherMock = new Mock<IMonitoringEventPublisher>();
        var workerRegistryMock = new Mock<IWorkerHealthRegistry>();

        var serviceScopeMock = new Mock<IServiceScope>();
        var scopeProviderMock = new Mock<IServiceProvider>();

        serviceScopeMock.Setup(x => x.ServiceProvider).Returns(scopeProviderMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(Mock.Of<IServiceScopeFactory>(f => f.CreateScope() == serviceScopeMock.Object));

        scopeProviderMock.Setup(x => x.GetService(typeof(IHealthCheckEngine))).Returns(healthCheckEngineMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IHealthCheckResultRepository))).Returns(resultRepoMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IUnitOfWork))).Returns(unitOfWorkMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IMonitoringEventPublisher))).Returns(publisherMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IWorkerHealthRegistry))).Returns(workerRegistryMock.Object);

        // Define a transition: previous state was Healthy, current is Unhealthy
        var previousResult = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 5);
        _healthProviderMock.Setup(x => x.GetComponentStatus("Database")).Returns(previousResult);

        var currentResult = new HealthCheckResult("Database", HealthStatus.Unhealthy, DateTime.UtcNow, 10, "DB_DOWN", "Connection failed");
        healthCheckEngineMock.Setup(x => x.RunAllChecksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HealthCheckResult> { currentResult });

        workerRegistryMock.Setup(x => x.GetWorkerHeartbeats()).Returns(new Dictionary<string, WorkerHeartbeat>());

        var worker = new MonitoringWorker(_healthProviderMock.Object, _serviceProviderMock.Object, _options, _loggerMock.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(50); // Stop loop quickly

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100);
            await worker.StopAsync(CancellationToken.None);
        }
        catch
        {
            // Ignore cancel exceptions
        }

        // Assert
        publisherMock.Verify(x => x.PublishAsync(
            It.Is<MonitoringEvent>(e => e.EventType == "HealthStatusChanged" && e.Severity == "ERROR" && e.Message.Contains("Database")),
            false,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task MonitoringWorker_ShouldNotEmitHealthStatusChanged_WhenStatusRemainsSame()
    {
        // Arrange
        var healthCheckEngineMock = new Mock<IHealthCheckEngine>();
        var resultRepoMock = new Mock<IHealthCheckResultRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var publisherMock = new Mock<IMonitoringEventPublisher>();
        var workerRegistryMock = new Mock<IWorkerHealthRegistry>();

        var serviceScopeMock = new Mock<IServiceScope>();
        var scopeProviderMock = new Mock<IServiceProvider>();

        serviceScopeMock.Setup(x => x.ServiceProvider).Returns(scopeProviderMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(Mock.Of<IServiceScopeFactory>(f => f.CreateScope() == serviceScopeMock.Object));

        scopeProviderMock.Setup(x => x.GetService(typeof(IHealthCheckEngine))).Returns(healthCheckEngineMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IHealthCheckResultRepository))).Returns(resultRepoMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IUnitOfWork))).Returns(unitOfWorkMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IMonitoringEventPublisher))).Returns(publisherMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IWorkerHealthRegistry))).Returns(workerRegistryMock.Object);

        // Previous state is Healthy, current state is ALSO Healthy
        var previousResult = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 5);
        _healthProviderMock.Setup(x => x.GetComponentStatus("Database")).Returns(previousResult);

        var currentResult = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 6);
        healthCheckEngineMock.Setup(x => x.RunAllChecksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HealthCheckResult> { currentResult });

        workerRegistryMock.Setup(x => x.GetWorkerHeartbeats()).Returns(new Dictionary<string, WorkerHeartbeat>());

        var worker = new MonitoringWorker(_healthProviderMock.Object, _serviceProviderMock.Object, _options, _loggerMock.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // Act
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100);
            await worker.StopAsync(CancellationToken.None);
        }
        catch
        {
            // Ignore cancel exceptions
        }

        // Assert - Should NOT publish HealthStatusChanged event
        publisherMock.Verify(x => x.PublishAsync(
            It.Is<MonitoringEvent>(e => e.EventType == "HealthStatusChanged"),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
