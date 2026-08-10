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
    private readonly ITradingGate? _tradingGate;
    private readonly ILogger<SignalStorageService> _logger;
    private readonly TradingBot.Application.Monitoring.IMetricsService? _generalMetrics;
    private readonly TradingBot.Application.Monitoring.IMonitoringEventPublisher? _monitoringEventPublisher;

    public SignalStorageService(
        ISignalRepository signalRepository,
        IUnitOfWork unitOfWork,
        ISignalStorageMetrics metrics,
        ILogger<SignalStorageService> logger,
        TradingBot.Application.Monitoring.IMetricsService? generalMetrics = null,
        TradingBot.Application.Monitoring.IMonitoringEventPublisher? monitoringEventPublisher = null,
        ITradingGate? tradingGate = null)
    {
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generalMetrics = generalMetrics;
        _monitoringEventPublisher = monitoringEventPublisher;
        _tradingGate = tradingGate;
    }

    public async Task StoreAsync(SignalCandidate candidate)
    {
        if (_tradingGate != null && (_tradingGate.CurrentState == TradingBot.Domain.Enums.ApplicationState.Stopping ||
                                     _tradingGate.CurrentState == TradingBot.Domain.Enums.ApplicationState.Stopped))
        {
            _logger.LogWarning("SignalStorageService: Discarding signal candidate because the application is stopping or stopped.");
            return;
        }

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
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException" ||
                                   ex.GetType().FullName == "Microsoft.EntityFrameworkCore.DbUpdateException" ||
                                   ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                                   ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
                                   ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("Concurrent duplicate signal detected and ignored. Channel: {ChannelId}, MessageId: {MessageId}",
                candidate.ChannelId, candidate.MessageId);

            _metrics.IncrementDuplicatesIgnored();
            _generalMetrics?.IncrementDuplicateSignals();

            try
            {
                await _unitOfWork.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback database transaction after duplicate signal violation.");
            }

            if (_monitoringEventPublisher != null)
            {
                var monitoringEvent = new TradingBot.Domain.Entities.MonitoringEvent(
                    "DuplicateSignalDetected",
                    "WARNING",
                    "SignalStorage",
                    "SignalStorageService",
                    "IGNORED",
                    $"Duplicate signal received and ignored: Channel={candidate.ChannelId}, MessageId={candidate.MessageId}",
                    signalId: signal.Id
                );
                await _monitoringEventPublisher.PublishAsync(monitoringEvent, forceSynchronous: true);
            }
            return;
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
