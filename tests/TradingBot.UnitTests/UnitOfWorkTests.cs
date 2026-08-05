using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.UnitTests;

public class UnitOfWorkTests : IDisposable
{
    private readonly TradingDbContext _dbContext;
    private readonly UnitOfWork _uow;

    public UnitOfWorkTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
        _uow = new UnitOfWork(_dbContext, NullLogger<UnitOfWork>.Instance);
    }

    public void Dispose()
    {
        _uow.Dispose();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldPersistAddedEntities()
    {
        // Arrange
        var order = new Order("CL-W1", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        await _dbContext.Orders.AddAsync(order);

        // Act
        var result = await _uow.SaveChangesAsync();

        // Assert
        result.Should().BeGreaterThan(0);
        var retrieved = await _dbContext.Orders.FindAsync(order.Id);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldWrapDbUpdateExceptionInDatabaseException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var mockDbContext = new Mock<TradingDbContext>(options);
        mockDbContext
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Duplicate key violation"));

        var uow = new UnitOfWork(mockDbContext.Object, NullLogger<UnitOfWork>.Instance);

        // Act & Assert
        Func<Task> act = async () => await uow.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DatabaseException>();
        ex.Which.Message.Should().Be("A database error occurred while saving changes. Please see inner exception for details.");
        ex.Which.InnerException.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldWrapUnexpectedExceptionInDatabaseException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var mockDbContext = new Mock<TradingDbContext>(options);
        mockDbContext
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Some random EF error"));

        var uow = new UnitOfWork(mockDbContext.Object, NullLogger<UnitOfWork>.Instance);

        // Act & Assert
        Func<Task> act = async () => await uow.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DatabaseException>();
        ex.Which.Message.Should().Be("An unexpected database error occurred. See inner exception for details.");
        ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }
}
