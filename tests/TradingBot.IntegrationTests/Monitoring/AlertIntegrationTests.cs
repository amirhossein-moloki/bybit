using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Monitoring;

public class AlertIntegrationTests
{
    private IServiceProvider CreateTestServiceProvider(out SqliteConnection outConnection)
    {
        var services = new ServiceCollection();

        // Register default .NET logging (registers ILogger<T> dependencies cleanly)
        services.AddLogging();

        // Fresh, completely isolated SQLite in-memory database
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        services.AddDbContext<TradingDbContext>(options =>
        {
            options.UseSqlite(connection);
        });

        // Register repositories & Unit Of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IAlertEventRepository, AlertEventRepository>();

        // Register AlertEngine
        services.AddScoped<IAlertEngine, AlertEngine>();

        // Mocks for decoupled layers
        var notificationEngineMock = new Mock<INotificationEngine>();
        services.AddSingleton(notificationEngineMock.Object);

        var metricsServiceMock = new Mock<IMetricsService>();
        services.AddSingleton(metricsServiceMock.Object);

        var loggerMock = new Mock<ILogger<AlertEngine>>();
        services.AddSingleton(loggerMock.Object);

        // Alert Options: omit Component from Rule so it matches any component
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
                    Component = null
                }
            }
        };
        services.AddSingleton(options);

        var provider = services.BuildServiceProvider();

        // Force EF Core to create the full isolated schema
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        context.Database.EnsureCreated();

        outConnection = connection;
        return provider;
    }

    [Fact]
    public async Task AlertEngine_ShouldPersistAndRecoverActiveAlerts_AfterApplicationRestart()
    {
        // Arrange
        var provider1 = CreateTestServiceProvider(out var connection);
        using (connection)
        {
            using var scope1 = provider1.CreateScope();
            var alertRepo1 = scope1.ServiceProvider.GetRequiredService<IAlertRepository>();
            var unitOfWork1 = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var deduplicationKey = $"RestartRecovery-{Guid.NewGuid():N}";

            var alert = new Alert(
                ruleId: "BybitDisconnected",
                alertType: "BybitDisconnected",
                severity: "ERROR",
                status: "Triggered",
                source: "Exchange",
                component: "BybitRest",
                message: "Rest connection lost",
                deduplicationKey: deduplicationKey
            );

            await alertRepo1.AddAsync(alert);
            await unitOfWork1.SaveChangesAsync();

            // Act - fresh scope simulation on the same connection
            using var scope2 = provider1.CreateScope();
            var alertRepo2 = scope2.ServiceProvider.GetRequiredService<IAlertRepository>();

            var recoveredAlert = await alertRepo2.GetActiveByDeduplicationKeyAsync(deduplicationKey);

            // Assert
            recoveredAlert.Should().NotBeNull();
            recoveredAlert!.Status.Should().Be("Triggered");
            recoveredAlert.Component.Should().Be("BybitRest");
            recoveredAlert.Message.Should().Be("Rest connection lost");
        }
    }

    [Fact]
    public async Task AlertEngine_ShouldPreventDuplicateActiveAlerts_UnderConcurrency()
    {
        // Arrange
        var provider = CreateTestServiceProvider(out var connection);
        using (connection)
        {
            using var scope = provider.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var deduplicationKey = $"ConcurrencyKey-{Guid.NewGuid():N}";

            var alert1 = new Alert(
                ruleId: "BybitDisconnected",
                alertType: "BybitDisconnected",
                severity: "ERROR",
                status: "Triggered",
                source: "Exchange",
                component: "Bybit",
                message: "First loss",
                deduplicationKey: deduplicationKey
            );

            var alert2 = new Alert(
                ruleId: "BybitDisconnected",
                alertType: "BybitDisconnected",
                severity: "ERROR",
                status: "Triggered",
                source: "Exchange",
                component: "Bybit",
                message: "Second loss",
                deduplicationKey: deduplicationKey
            );

            // Act
            await alertRepo.AddAsync(alert1);
            await unitOfWork.SaveChangesAsync();

            // Adding second active alert with same deduplication key should throw due to database index uniqueness constraint
            Func<Task> act = async () =>
            {
                using var scope2 = provider.CreateScope();
                var alertRepo2 = scope2.ServiceProvider.GetRequiredService<IAlertRepository>();
                var uow2 = scope2.ServiceProvider.GetRequiredService<IUnitOfWork>();

                await alertRepo2.AddAsync(alert2);
                await uow2.SaveChangesAsync();
            };

            // Assert
            await act.Should().ThrowAsync<Exception>();
        }
    }

    [Fact]
    public async Task AlertEngine_ShouldHandleTriggerAndRecovery_EndToEnd()
    {
        // Arrange
        var provider = CreateTestServiceProvider(out var connection);
        using (connection)
        {
            using var scope = provider.CreateScope();
            var alertEngine = scope.ServiceProvider.GetRequiredService<IAlertEngine>();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();

            var component = $"BybitRest-{Guid.NewGuid():N}";

            var disconnectEvent = new MonitoringEvent(
                eventType: "BybitDisconnected",
                severity: "ERROR",
                source: "Exchange",
                component: component,
                status: "Disconnected",
                message: "Rest connection lost"
            );

            var restoreEvent = new MonitoringEvent(
                eventType: "BybitConnectionRestored",
                severity: "INFORMATION",
                source: "Exchange",
                component: component,
                status: "Connected",
                message: "Rest connection restored"
            );

            // Act - Trigger Alert
            await alertEngine.ProcessEventAsync(disconnectEvent);

            // Assert Triggered
            var activeAlerts = await alertRepo.GetActiveAlertsAsync();
            var alert = activeAlerts.FirstOrDefault(a => a.Component == component);
            alert.Should().NotBeNull();
            alert!.Status.Should().Be("Triggered");

            // Act - Restore / Recover Condition
            await alertEngine.ProcessEventAsync(restoreEvent);

            // Assert Resolved
            using var scope2 = provider.CreateScope();
            var alertRepo2 = scope2.ServiceProvider.GetRequiredService<IAlertRepository>();
            var activeAlertsAfter = await alertRepo2.GetActiveAlertsAsync();
            var resolvedAlert = activeAlertsAfter.FirstOrDefault(a => a.Component == component);
            resolvedAlert.Should().BeNull(); // No longer active!

            var allAlerts = await alertRepo2.GetAllAsync();
            var finalAlert = allAlerts.FirstOrDefault(a => a.Component == component);
            finalAlert.Should().NotBeNull();
            finalAlert!.Status.Should().Be("Resolved");
            finalAlert.ResolvedAt.Should().NotBeNull();
        }
    }
}
