using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Infrastructure.Monitoring.Checks;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Monitoring;

public class MonitoringIntegrationTests : IAsyncLifetime, IClassFixture<CustomWebApplicationFactory>
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private readonly CustomWebApplicationFactory _factory;

    public MonitoringIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();
        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        // Clear static state to isolate this test run from other tests
        HealthCheckEngine.ResetState();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }

        if (_sqliteConnection != null)
        {
            await _sqliteConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task MonitoringWorker_ShouldPersistResultsAndExposeCurrentStatus()
    {
        // Arrange
        var mockCheck = new Mock<IHealthCheck>();
        mockCheck.Setup(c => c.Name).Returns("Database");
        mockCheck.Setup(c => c.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 5, metadata: "{\"ResponseTimeMs\":5}"));

        var options = new MonitoringOptions();
        options.Workers.IntervalSeconds = 1;
        options.Database.IntervalSeconds = 0; // Force immediate run

        var statusProvider = new HealthStatusProvider();
        var loggerEngine = NullLogger<HealthCheckEngine>.Instance;
        var healthCheckEngine = new HealthCheckEngine(new[] { mockCheck.Object }, options, loggerEngine);

        var repository = new HealthCheckResultRepository(_dbContext!);
        var unitOfWork = new UnitOfWork(_dbContext!, NullLogger<UnitOfWork>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton<IHealthCheckEngine>(healthCheckEngine);
        services.AddSingleton<IHealthCheckResultRepository>(repository);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        var serviceProvider = services.BuildServiceProvider();

        var workerLogger = NullLogger<TradingBot.Worker.MonitoringWorker>.Instance;
        var worker = new TradingBot.Worker.MonitoringWorker(
            statusProvider,
            serviceProvider,
            options,
            workerLogger
        );

        using var cts = new CancellationTokenSource();

        // Act
        var runTask = worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(cts.Token);

        // Assert
        // 1. Current status provider is updated
        statusProvider.GetOverallStatus().Should().Be(HealthStatus.Healthy);
        var dbResult = statusProvider.GetComponentStatus("Database");
        dbResult.Should().NotBeNull();
        dbResult!.Status.Should().Be(HealthStatus.Healthy);

        // 2. Persisted in DB
        var records = await _dbContext!.HealthCheckResults.ToListAsync();
        records.Should().NotBeEmpty();
        records.First().ServiceName.Should().Be("Database");
        records.First().Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task HealthStatusEndpoint_ShouldReturnCorrectJson()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\"");
        content.Should().Contain("\"components\"");
    }
}
