using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Application.Services;

public class SignalStorageService : ISignalStorageService
{
    private readonly ISignalRepository _signalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISignalStorageMetrics _metrics;
    private readonly ILogger<SignalStorageService> _logger;

    public SignalStorageService(
        ISignalRepository signalRepository,
        IUnitOfWork unitOfWork,
        ISignalStorageMetrics metrics,
        ILogger<SignalStorageService> logger)
    {
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StoreAsync(SignalCandidate candidate)
    {
        // 1. Invalid Signal Candidate Check
        if (candidate == null)
        {
            _metrics.IncrementStorageFailures();
            _logger.LogError("Storage failed: Signal candidate is null.");
            throw new ArgumentNullException(nameof(candidate), "Signal candidate cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(candidate.DetectedSymbol))
        {
            _metrics.IncrementStorageFailures();
            _logger.LogWarning("Rejected: Signal candidate has no detected symbol. Channel: {ChannelId}, MessageId: {MessageId}",
                candidate.ChannelId, candidate.MessageId);
            throw new ArgumentException("Signal candidate must have a detected symbol.", nameof(candidate));
        }

        // 2. Check Duplicate
        try
        {
            var exists = await _signalRepository.ExistsAsync(candidate.ChannelId, candidate.MessageId);
            if (exists)
            {
                _metrics.IncrementDuplicatesIgnored();
                _logger.LogInformation("Duplicate signal ignored\nChannel:\n{ChannelId}\n\nMessageId:\n{MessageId}",
                    candidate.ChannelId, candidate.MessageId);
                return;
            }
        }
        catch (Exception ex)
        {
            _metrics.IncrementStorageFailures();
            _logger.LogError(ex, "Storage failed during duplicate check. Channel: {ChannelId}, MessageId: {MessageId}",
                candidate.ChannelId, candidate.MessageId);
            throw;
        }

        // 3. Map to Signal Entity
        OrderSide side = OrderSide.Buy;
        if (!string.IsNullOrEmpty(candidate.DetectedSide))
        {
            if (candidate.DetectedSide.Equals("SHORT", StringComparison.OrdinalIgnoreCase) ||
                candidate.DetectedSide.Equals("SELL", StringComparison.OrdinalIgnoreCase))
            {
                side = OrderSide.Sell;
            }
        }

        Signal signal;
        try
        {
            signal = new Signal(
                candidate.ChannelId,
                candidate.MessageId,
                candidate.RawText,
                candidate.DetectedSymbol,
                side,
                candidate.DetectedAt
            );
        }
        catch (DomainException ex)
        {
            _metrics.IncrementStorageFailures();
            _logger.LogWarning(ex, "Rejected: Failed to map candidate to valid domain signal. Channel: {ChannelId}, MessageId: {MessageId}",
                candidate.ChannelId, candidate.MessageId);
            throw;
        }

        // 4. Atomic Transaction Storage
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _signalRepository.SaveAsync(signal);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitAsync();

            _metrics.IncrementSignalsStored();
            _logger.LogInformation("Signal stored\nChannel:\n{ChannelId}\n\nMessageId:\n{MessageId}",
                candidate.ChannelId, candidate.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database Failure: Failed to commit signal storage transaction. Rolling back. Channel: {ChannelId}, MessageId: {MessageId}",
                candidate.ChannelId, candidate.MessageId);

            _metrics.IncrementStorageFailures();

            try
            {
                await _unitOfWork.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback database transaction after save failure.");
            }

            throw;
        }
    }
}
