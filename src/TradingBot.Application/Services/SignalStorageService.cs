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
    private readonly IServiceProvider? _serviceProvider;

    public SignalStorageService(
        ISignalRepository signalRepository,
        IUnitOfWork unitOfWork,
        ISignalStorageMetrics metrics,
        ILogger<SignalStorageService> logger,
        TradingBot.Application.Monitoring.IMetricsService? generalMetrics = null,
        TradingBot.Application.Monitoring.IMonitoringEventPublisher? monitoringEventPublisher = null,
        ITradingGate? tradingGate = null,
        IServiceProvider? serviceProvider = null)
    {
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generalMetrics = generalMetrics;
        _monitoringEventPublisher = monitoringEventPublisher;
        _tradingGate = tradingGate;
        _serviceProvider = serviceProvider;
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

                await SendNotificationAsync(
                    eventType: "DuplicateSignal",
                    severity: "WARNING",
                    title: "Duplicate Signal Ignored",
                    status: "⚠️ Duplicate Signal Ignored",
                    reason: $"Message ID {candidate.MessageId} from this source was already processed previously. Ignored to avoid duplicate execution.",
                    channelId: candidate.ChannelId,
                    messageId: candidate.MessageId,
                    date: candidate.DetectedAt,
                    rawText: candidate.RawText,
                    symbol: candidate.DetectedSymbol,
                    side: candidate.DetectedSide
                );
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

            // Execute downstream Signal Intelligence, Risk Evaluation, and Trade Execution pipeline
            await ProcessSignalPipelineAsync(signal, candidate);
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

    private async Task ProcessSignalPipelineAsync(Signal signal, SignalCandidate candidate)
    {
        _logger.LogInformation("SignalPipelineStarted: Triggering parsing, risk evaluation, and execution pipeline for SignalId {SignalId}", signal.Id);

        try
        {
            decimal entryPrice = 1.0m;
            decimal? stopLoss = null;
            var takeProfits = new System.Collections.Generic.List<decimal>();
            int? leverage = 10;

            // 1. Direct Regex extraction on RawText for Entry, SL, TP, Leverage
            var entryMatch = System.Text.RegularExpressions.Regex.Match(candidate.RawText, @"(?:نقطه\s*ورود|ENTRY|ورود|BUY\s*ZONE)[\s:]*([0-9.,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (entryMatch.Success && decimal.TryParse(entryMatch.Groups[1].Value.Replace(",", ""), out var parsedEntry))
            {
                entryPrice = parsedEntry;
            }

            var slMatch = System.Text.RegularExpressions.Regex.Match(candidate.RawText, @"(?:حد\s*ضرر|\bSL\b|STOP\s*LOSS)(?:\s*\([^)]*\))?[\s:]*([0-9.,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (slMatch.Success && decimal.TryParse(slMatch.Groups[1].Value.Replace(",", ""), out var parsedSl))
            {
                stopLoss = parsedSl;
            }

            var tpMatches = System.Text.RegularExpressions.Regex.Matches(candidate.RawText, @"(?:تارگت|TP|TARGET)[\s\w]*[\s:]*([0-9.,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in tpMatches)
            {
                if (decimal.TryParse(m.Groups[1].Value.Replace(",", ""), out var parsedTp))
                {
                    takeProfits.Add(parsedTp);
                }
            }

            var levMatch = System.Text.RegularExpressions.Regex.Match(candidate.RawText, @"(?:\bLEVERAGE[\s:]*|LEVERAGE\s*|\b)([0-9]+)[xX]?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (levMatch.Success && int.TryParse(levMatch.Groups[1].Value, out var parsedLev) && parsedLev > 0)
            {
                leverage = parsedLev;
            }

            if (entryPrice > 0m)
            {
                signal.UpdateParsedDetails(signal.Symbol, signal.Side, entryPrice, stopLoss, takeProfits.FirstOrDefault(), leverage);
            }

            // Mark Parsed and Validated
            signal.MarkParsed();
            signal.MarkValidated();
            _signalRepository.Update(signal);
            await _unitOfWork.SaveChangesAsync();

            // 2. Risk Evaluation Workflow
            var riskWorkflow = _serviceProvider?.GetService(typeof(TradingBot.Application.RiskManagement.Workflow.ITradeDecisionWorkflow))
                as TradingBot.Application.RiskManagement.Workflow.ITradeDecisionWorkflow;

            if (riskWorkflow != null)
            {
                var effectiveEntry = signal.EntryPrice > 0 ? signal.EntryPrice : 1.0m;
                var effectiveSl = signal.StopLoss ?? (signal.Side == OrderSide.Buy ? effectiveEntry * 0.99m : effectiveEntry * 1.01m);

                var tradeRiskContext = new TradingBot.Domain.RiskManagement.ValueObjects.TradeRiskContext
                {
                    SignalId = signal.Id,
                    Symbol = signal.Symbol,
                    Side = signal.Side,
                    EntryPrice = effectiveEntry,
                    StopLoss = effectiveSl,
                    TakeProfits = takeProfits.Any() ? takeProfits : (signal.TakeProfit.HasValue ? new System.Collections.Generic.List<decimal> { signal.TakeProfit.Value } : new System.Collections.Generic.List<decimal> { signal.Side == OrderSide.Buy ? effectiveEntry * 1.01m : effectiveEntry * 0.99m }),
                    Leverage = signal.Leverage ?? 10,
                    AccountBalance = 1000m,
                    OpenPositions = 0,
                    DailyPnL = 0m,
                    CurrentExposure = 0m
                };

                var workflowContext = new TradingBot.Application.RiskManagement.Workflow.RiskWorkflowContext(signal, tradeRiskContext);
                var workflowResult = await riskWorkflow.ExecuteAsync(workflowContext);

                _logger.LogInformation("RiskEvaluationCompleted: SignalId {SignalId}, Decision: {Decision}, Message: {Message}",
                    signal.Id, workflowResult.TradeDecision?.Decision, workflowResult.Message);

                bool isRiskApproved = workflowResult.IsSuccess &&
                                       workflowResult.TradeDecision != null &&
                                       workflowResult.TradeDecision.Decision == TradingBot.Domain.RiskManagement.Enums.RiskDecisionStatus.Approved;

                if (!isRiskApproved)
                {
                    var riskReason = !string.IsNullOrWhiteSpace(workflowResult.Message)
                        ? workflowResult.Message
                        : $"Risk check resulted in '{workflowResult.TradeDecision?.Decision}'. Trade not approved.";

                    await SendNotificationAsync(
                        eventType: "RiskRejected",
                        severity: "WARNING",
                        title: $"Risk Check Rejected: {signal.Symbol}",
                        status: "⛔ Risk Check Rejected",
                        reason: riskReason,
                        channelId: candidate.ChannelId,
                        messageId: candidate.MessageId,
                        date: candidate.DetectedAt,
                        rawText: candidate.RawText,
                        symbol: signal.Symbol,
                        side: signal.Side.ToString(),
                        entryPrice: effectiveEntry,
                        stopLoss: effectiveSl,
                        takeProfit: signal.TakeProfit ?? takeProfits.FirstOrDefault(),
                        leverage: leverage
                    );
                }

                // 3. Trade Execution Orchestrator
                if (isRiskApproved)
                {
                    var orchestrator = _serviceProvider?.GetService(typeof(TradingBot.Application.Trading.Execution.Contracts.ITradeExecutionOrchestrator))
                        as TradingBot.Application.Trading.Execution.Contracts.ITradeExecutionOrchestrator;

                    if (orchestrator != null)
                    {
                        var rawQty = workflowResult.RiskEvaluation?.PositionSize > 0
                            ? workflowResult.RiskEvaluation.PositionSize
                            : 1.0m;
                        var qty = Math.Min(100m, Math.Max(0.01m, Math.Round(rawQty, 2)));

                        var executionRequest = new TradingBot.Application.Trading.Execution.Models.TradeExecutionRequest
                        {
                            SignalId = signal.Id,
                            RiskEvaluationId = workflowResult.RiskEvaluation?.Id ?? Guid.NewGuid(),
                            Symbol = signal.Symbol,
                            Side = signal.Side,
                            OrderType = OrderType.Limit,
                            Quantity = qty,
                            Price = effectiveEntry,
                            RiskDecision = TradingBot.Domain.RiskManagement.Enums.RiskDecisionStatus.Approved
                        };

                        var executionResult = await orchestrator.OrchestrateAsync(executionRequest);
                        _logger.LogInformation("TradeExecutionCompleted: SignalId {SignalId}, Success: {Success}, OrderId: {OrderId}, Status: {Status}, Reason: {Reason}",
                            signal.Id, executionResult.Success, executionResult.OrderId, executionResult.Status, executionResult.FailureReason);

                        if (executionResult.Success)
                        {
                            await _unitOfWork.BeginTransactionAsync();
                            signal.MarkExecuted();
                            _signalRepository.Update(signal);
                            await _unitOfWork.SaveChangesAsync();
                            await _unitOfWork.CommitTransactionAsync();

                            await SendNotificationAsync(
                                eventType: "TradeExecutedFromSignal",
                                severity: "INFORMATION",
                                title: $"Trade Executed: {signal.Symbol}",
                                status: "✅ Trade Created & Order Executed",
                                reason: "Signal passed all validations and risk checks. Order successfully submitted to Bybit.",
                                channelId: candidate.ChannelId,
                                messageId: candidate.MessageId,
                                date: candidate.DetectedAt,
                                rawText: candidate.RawText,
                                symbol: signal.Symbol,
                                side: signal.Side.ToString(),
                                entryPrice: effectiveEntry,
                                stopLoss: effectiveSl,
                                takeProfit: signal.TakeProfit ?? takeProfits.FirstOrDefault(),
                                leverage: leverage,
                                orderId: executionResult.OrderId.ToString()
                            );
                        }
                        else
                        {
                            await SendNotificationAsync(
                                eventType: "TradeExecutionFailed",
                                severity: "ERROR",
                                title: $"Trade Execution Failed: {signal.Symbol}",
                                status: "❌ Exchange Execution Failed",
                                reason: string.IsNullOrWhiteSpace(executionResult.FailureReason) ? "Order submission to exchange failed." : executionResult.FailureReason,
                                channelId: candidate.ChannelId,
                                messageId: candidate.MessageId,
                                date: candidate.DetectedAt,
                                rawText: candidate.RawText,
                                symbol: signal.Symbol,
                                side: signal.Side.ToString(),
                                entryPrice: effectiveEntry,
                                stopLoss: effectiveSl,
                                takeProfit: signal.TakeProfit ?? takeProfits.FirstOrDefault(),
                                leverage: leverage
                            );
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during signal parsing, risk evaluation, and trade execution pipeline for SignalId {SignalId}", signal.Id);
        }
    }

    private async Task SendNotificationAsync(
        string eventType,
        string severity,
        string title,
        string status,
        string reason,
        long channelId,
        long messageId,
        DateTime date,
        string rawText,
        string? symbol = null,
        string? side = null,
        decimal? entryPrice = null,
        decimal? stopLoss = null,
        decimal? takeProfit = null,
        int? leverage = null,
        string? orderId = null)
    {
        try
        {
            if (_serviceProvider == null) return;

            var notifOptions = _serviceProvider.GetService(typeof(Microsoft.Extensions.Options.IOptions<TradingBot.Application.Monitoring.Configuration.NotificationOptions>))
                as Microsoft.Extensions.Options.IOptions<TradingBot.Application.Monitoring.Configuration.NotificationOptions>;
            var recipient = notifOptions?.Value?.Telegram?.ChatId;

            if (string.IsNullOrWhiteSpace(recipient) || recipient == "-1234567890" || recipient == "1234567890" || recipient == "default-chat-id")
            {
                return;
            }

            var notifRepo = _serviceProvider.GetService(typeof(INotificationRepository)) as INotificationRepository;
            var unitOfWork = _serviceProvider.GetService(typeof(IUnitOfWork)) as IUnitOfWork;

            if (notifRepo != null && unitOfWork != null)
            {
                var sourceTitle = $"Chat {channelId}";

                var payloadDict = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["Status"] = status,
                    ["Reason"] = reason,
                    ["SourceTitle"] = sourceTitle,
                    ["ChatId"] = channelId,
                    ["MessageId"] = messageId,
                    ["RawText"] = rawText,
                    ["Symbol"] = symbol,
                    ["Side"] = side,
                    ["EntryPrice"] = entryPrice?.ToString(),
                    ["StopLoss"] = stopLoss?.ToString(),
                    ["TakeProfit"] = takeProfit?.ToString(),
                    ["Leverage"] = leverage?.ToString(),
                    ["OrderId"] = orderId
                };

                var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadDict);

                var messageBuilder = _serviceProvider.GetService(typeof(TradingBot.Application.Monitoring.ITelegramMessageBuilder))
                    as TradingBot.Application.Monitoring.ITelegramMessageBuilder;
                string formattedMsg;

                if (messageBuilder != null)
                {
                    var evt = new TradingBot.Domain.Entities.MonitoringEvent(
                        eventType: eventType,
                        severity: severity,
                        source: sourceTitle,
                        component: "SignalIntelligence",
                        status: status,
                        message: reason,
                        payload: payloadJson
                    );
                    formattedMsg = messageBuilder.BuildMessage(evt);
                }
                else
                {
                    formattedMsg = $"📩 <b>[Telegram Intercepted Message]</b>\n" +
                                   $"<b>Source:</b> {sourceTitle} (<code>{channelId}</code>)\n" +
                                   $"<b>Message ID:</b> <code>{messageId}</code>\n" +
                                   $"<b>Time:</b> {date:yyyy-MM-dd HH:mm:ss} UTC\n\n" +
                                   $"<b>Status:</b> {status}\n" +
                                   $"<b>Reason:</b> {reason}\n\n" +
                                   $"<b>Content:</b>\n<i>{rawText}</i>";
                }

                var notification = new TradingBot.Domain.Entities.Notification(
                    eventId: Guid.NewGuid(),
                    eventType: eventType,
                    severity: severity,
                    channel: "Telegram",
                    recipient: recipient,
                    title: title,
                    message: formattedMsg,
                    payload: payloadJson,
                    maxAttempts: notifOptions?.Value?.Telegram?.RetryCount ?? 3
                );

                await notifRepo.AddAsync(notification);
                await unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for signal candidate Message ID {MessageId}", messageId);
        }
    }
}
