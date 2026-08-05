using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Repositories;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly TradingDbContext _dbContext;
    private readonly ILogger<UnitOfWork> _logger;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(TradingDbContext dbContext, ILogger<UnitOfWork> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "A database update error occurred during SaveChanges.");
            throw new DatabaseException("A database error occurred while saving changes. Please see inner exception for details.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected database error occurred during SaveChanges.");
            throw new DatabaseException("An unexpected database error occurred. See inner exception for details.", ex);
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }

        try
        {
            _currentTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin database transaction.");
            throw new TransactionException("Failed to initiate database transaction. See inner exception.", ex);
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await CommitAsync(cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        await RollbackAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit database transaction.");
            throw new TransactionException("An error occurred while committing the transaction. Transaction rolled back.", ex);
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback database transaction.");
            throw new TransactionException("An error occurred while rolling back the transaction.", ex);
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _currentTransaction = null;
        GC.SuppressFinalize(this);
    }
}
