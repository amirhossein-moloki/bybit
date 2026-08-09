using System;
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
using TradingBot.Application.Exceptions;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Exceptions;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class MonitoringEventPublisherTests
{
    private readonly Mock<IMonitoringEventQueue> _queueMock = new();
    private readonly Mock<IEventSanitizer> _sanitizerMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<MonitoringEventPublisher>> _loggerMock = new();
    private readonly MonitoringOptions _options = new();

    public MonitoringEventPublisherTests()
    {
        _sanitizerMock.Setup(x => x.Sanitize(It.IsAny<string>())).Returns<string>(s => s);
        _sanitizerMock.Setup(x => x.SanitizeAndLimit(It.IsAny<string>(), It.IsAny<int>())).Returns<string, int>((s, l) => s);
    }

    [Fact]
    public async Task PublishAsync_ShouldEnqueueEvent_WhenForceSynchronousIsFalse()
    {
        // Arrange
        var publisher = new TradingBot.Application.Monitoring.Services.MonitoringEventPublisher(
            _queueMock.Object, _sanitizerMock.Object, _options, _serviceProviderMock.Object, _loggerMock.Object);

        var @event = new MonitoringEvent("TestType", "INFO", "Test", "Test", "Test", "TestMessage");

        // Act
        await publisher.PublishAsync(@event, forceSynchronous: false, CancellationToken.None);

        // Assert
        _queueMock.Verify(x => x.EnqueueAsync(It.IsAny<MonitoringEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ShouldWriteToRepositoryDirectly_WhenForceSynchronousIsTrue()
    {
        // Arrange
        var repoMock = new Mock<IMonitoringEventRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var serviceScopeMock = new Mock<IServiceScope>();
        var scopeProviderMock = new Mock<IServiceProvider>();

        serviceScopeMock.Setup(x => x.ServiceProvider).Returns(scopeProviderMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(Mock.Of<IServiceScopeFactory>(f => f.CreateScope() == serviceScopeMock.Object));

        scopeProviderMock.Setup(x => x.GetService(typeof(IMonitoringEventRepository))).Returns(repoMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IUnitOfWork))).Returns(unitOfWorkMock.Object);

        var publisher = new TradingBot.Application.Monitoring.Services.MonitoringEventPublisher(
            _queueMock.Object, _sanitizerMock.Object, _options, _serviceProviderMock.Object, _loggerMock.Object);

        var @event = new MonitoringEvent("TestType", "INFO", "Test", "Test", "Test", "TestMessage");

        // Act
        await publisher.PublishAsync(@event, forceSynchronous: true, CancellationToken.None);

        // Assert
        repoMock.Verify(x => x.AddAsync(It.IsAny<MonitoringEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _queueMock.Verify(x => x.EnqueueAsync(It.IsAny<MonitoringEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_ShouldNotCrash_WhenRepositoryThrowsExceptionOnForceSynchronous()
    {
        // Arrange
        var repoMock = new Mock<IMonitoringEventRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var serviceScopeMock = new Mock<IServiceScope>();
        var scopeProviderMock = new Mock<IServiceProvider>();

        serviceScopeMock.Setup(x => x.ServiceProvider).Returns(scopeProviderMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(Mock.Of<IServiceScopeFactory>(f => f.CreateScope() == serviceScopeMock.Object));

        scopeProviderMock.Setup(x => x.GetService(typeof(IMonitoringEventRepository))).Returns(repoMock.Object);
        scopeProviderMock.Setup(x => x.GetService(typeof(IUnitOfWork))).Returns(unitOfWorkMock.Object);

        // Make repo save fail
        repoMock.Setup(x => x.AddAsync(It.IsAny<MonitoringEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DatabaseException("DB failure"));

        var publisher = new TradingBot.Application.Monitoring.Services.MonitoringEventPublisher(
            _queueMock.Object, _sanitizerMock.Object, _options, _serviceProviderMock.Object, _loggerMock.Object);

        var @event = new MonitoringEvent("TestType", "INFO", "Test", "Test", "Test", "TestMessage");

        // Act
        Func<Task> act = async () => await publisher.PublishAsync(@event, forceSynchronous: true, CancellationToken.None);

        // Assert - Should isolate failure and NOT throw/crash (Section 49)
        await act.Should().NotThrowAsync();
    }
}
