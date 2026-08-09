using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.IntegrationTests.Monitoring;

public class MonitoringObservabilityIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MonitoringObservabilityIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    [Fact]
    public async Task EventPublishingPipeline_ShouldPublishEnqueueAndPersistEvent_EndToEnd()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMonitoringEventPublisher>();
        var reader = scope.ServiceProvider.GetRequiredService<IMonitoringEventReader>();

        var testMessage = $"E2E Test Message: {Guid.NewGuid()}";
        var correlationId = Guid.NewGuid().ToString();

        var @event = new MonitoringEvent(
            eventType: "SignalReceived",
            severity: "INFORMATION",
            source: "Telegram",
            component: "ParserPipeline",
            status: "Detected",
            message: testMessage,
            correlationId: correlationId
        );

        // Act
        await publisher.PublishAsync(@event, forceSynchronous: false);

        // Wait up to 3 seconds for background worker to consume and persist the event
        PagedResult<MonitoringEvent>? result = null;
        for (int i = 0; i < 30; i++)
        {
            result = await reader.GetEventsAsync(correlationId: correlationId);
            if (result.Items.Any())
            {
                break;
            }
            await Task.Delay(100);
        }

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        var persistedEvent = result.Items.First();
        persistedEvent.Message.Should().Be(testMessage);
        persistedEvent.EventType.Should().Be("SignalReceived");
        persistedEvent.Severity.Should().Be("INFORMATION");
        persistedEvent.Source.Should().Be("Telegram");
        persistedEvent.Component.Should().Be("ParserPipeline");
        persistedEvent.Status.Should().Be("Detected");
        persistedEvent.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task EventQuery_ShouldSupportPaginationAndFiltering()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMonitoringEventPublisher>();
        var reader = scope.ServiceProvider.GetRequiredService<IMonitoringEventReader>();

        var testGroup = $"Group-{Guid.NewGuid():N}";

        // Publish 5 events sequentially
        for (int i = 1; i <= 5; i++)
        {
            var @event = new MonitoringEvent(
                eventType: "TestQueryEvent",
                severity: i % 2 == 0 ? "ERROR" : "INFORMATION",
                source: "QueryTest",
                component: testGroup,
                status: "Succeeded",
                message: $"Message {i} for {testGroup}",
                correlationId: testGroup,
                timestamp: DateTime.UtcNow.AddMinutes(i) // Ensure strict distinct timestamps
            );
            await publisher.PublishAsync(@event, forceSynchronous: true); // Force direct save to bypass wait
        }

        // Act - Query Page 1
        var page1 = await reader.GetEventsAsync(
            eventType: "TestQueryEvent",
            correlationId: testGroup,
            pageNumber: 1,
            pageSize: 3
        );

        // Act - Query Page 2
        var page2 = await reader.GetEventsAsync(
            eventType: "TestQueryEvent",
            correlationId: testGroup,
            pageNumber: 2,
            pageSize: 3
        );

        // Act - Query Filtered by Severity
        var errorPage = await reader.GetEventsAsync(
            eventType: "TestQueryEvent",
            severity: "ERROR",
            correlationId: testGroup
        );

        // Assert
        page1.Items.Should().HaveCount(3);
        page1.TotalCount.Should().Be(5);
        page1.PageNumber.Should().Be(1);
        page1.PageSize.Should().Be(3);

        // Verify Newest First Order (Message 5 should be first)
        page1.Items.First().Message.Should().Be($"Message 5 for {testGroup}");

        page2.Items.Should().HaveCount(2);
        page2.Items.First().Message.Should().Be($"Message 2 for {testGroup}");

        errorPage.Items.Should().HaveCount(2);
        errorPage.Items.All(x => x.Severity == "ERROR").Should().BeTrue();
    }

    [Fact]
    public async Task FullTradeTrace_ShouldPreserveCorrelation_AcrossEntireLifecycle()
    {
        // Arrange (Section 68 - Full Trade Trace)
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMonitoringEventPublisher>();
        var reader = scope.ServiceProvider.GetRequiredService<IMonitoringEventReader>();

        var correlationId = $"Trace-{Guid.NewGuid():N}";
        var signalId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        // 1. Signal Received
        await publisher.PublishAsync(new MonitoringEvent("SignalReceived", "INFORMATION", "Telegram", "Receiver", "Detected", "Signal received.", correlationId: correlationId, signalId: signalId), forceSynchronous: true);

        // 2. Signal Accepted
        await publisher.PublishAsync(new MonitoringEvent("SignalAccepted", "INFORMATION", "SignalParser", "Pipeline", "Succeeded", "Signal accepted.", correlationId: correlationId, signalId: signalId), forceSynchronous: true);

        // 3. Order Created
        await publisher.PublishAsync(new MonitoringEvent("OrderCreated", "INFORMATION", "ExecutionEngine", "OrderBuilder", "Succeeded", "Order created from signal.", correlationId: correlationId, signalId: signalId, orderId: orderId), forceSynchronous: true);

        // 4. Order Submitted
        await publisher.PublishAsync(new MonitoringEvent("OrderSubmitted", "INFORMATION", "Bybit", "OrderExecution", "Succeeded", "Order submitted to Bybit.", correlationId: correlationId, signalId: signalId, orderId: orderId), forceSynchronous: true);

        // 5. Order Filled
        await publisher.PublishAsync(new MonitoringEvent("OrderFilled", "INFORMATION", "Bybit", "OrderSyncBackgroundService", "Succeeded", "Order filled at Bybit.", correlationId: correlationId, signalId: signalId, orderId: orderId), forceSynchronous: true);

        // 6. Position Opened
        await publisher.PublishAsync(new MonitoringEvent("PositionOpened", "INFORMATION", "PositionManager", "PositionService", "Succeeded", "Position opened.", correlationId: correlationId, signalId: signalId, orderId: orderId, positionId: positionId), forceSynchronous: true);

        // 7. Position Updated
        await publisher.PublishAsync(new MonitoringEvent("PositionUpdated", "INFORMATION", "PositionManager", "StopLossManager", "Succeeded", "Position Stop Loss updated.", correlationId: correlationId, signalId: signalId, orderId: orderId, positionId: positionId), forceSynchronous: true);

        // 8. Position Closed
        await publisher.PublishAsync(new MonitoringEvent("PositionClosed", "INFORMATION", "PositionManager", "PositionCloseManager", "Succeeded", "Position closed.", correlationId: correlationId, signalId: signalId, orderId: orderId, positionId: positionId), forceSynchronous: true);

        // Act
        var traceEvents = await reader.GetEventsAsync(correlationId: correlationId, pageSize: 20);

        // Assert
        traceEvents.Items.Should().HaveCount(8);

        // Verify order of events matches newest-first (PositionClosed down to SignalReceived)
        var eventsList = traceEvents.Items.ToList();
        eventsList[0].EventType.Should().Be("PositionClosed");
        eventsList[1].EventType.Should().Be("PositionUpdated");
        eventsList[2].EventType.Should().Be("PositionOpened");
        eventsList[3].EventType.Should().Be("OrderFilled");
        eventsList[4].EventType.Should().Be("OrderSubmitted");
        eventsList[5].EventType.Should().Be("OrderCreated");
        eventsList[6].EventType.Should().Be("SignalAccepted");
        eventsList[7].EventType.Should().Be("SignalReceived");

        // Verify all have identical correlation ID and link correctly
        foreach (var ev in eventsList)
        {
            ev.CorrelationId.Should().Be(correlationId);
            ev.SignalId.Should().Be(signalId);
            if (ev.EventType != "SignalReceived" && ev.EventType != "SignalAccepted")
            {
                ev.OrderId.Should().Be(orderId);
            }
            if (ev.EventType.StartsWith("Position"))
            {
                ev.PositionId.Should().Be(positionId);
            }
        }
    }
}
