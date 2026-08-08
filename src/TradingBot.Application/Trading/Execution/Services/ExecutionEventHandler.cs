using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Events;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Services;

public class ExecutionEventHandler : IExecutionEventHandler
{
    private readonly ILogger<ExecutionEventHandler> _logger;
    private readonly ISystemLogRepository _systemLogRepository;
    private readonly IExecutionMetrics _metrics;

    public ExecutionEventHandler(
        ILogger<ExecutionEventHandler> logger,
        ISystemLogRepository systemLogRepository,
        IExecutionMetrics metrics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _systemLogRepository = systemLogRepository ?? throw new ArgumentNullException(nameof(systemLogRepository));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async Task HandleAsync(IExecutionEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) return;

        // 1. Structured Logging (Section 9)
        _logger.LogInformation("ExecutionEvent: {EventType}. ExecutionId: {ExecutionId}, OrderId: {OrderId}, SignalId: {SignalId}, Symbol: {Symbol}, Status: {Status}, Duration: {DurationMs}ms",
            @event.GetType().Name, @event.ExecutionId, @event.OrderId, @event.SignalId, @event.Symbol, @event.Status, @event.Duration.TotalMilliseconds);

        // 2. Metrics Tracking (Section 8)
        switch (@event)
        {
            case TradeExecutionStartedEvent:
                // We only increment TotalExecutions on completion to prevent double-counting
                break;
            case OrderSubmissionStartedEvent:
                break;
            case OrderSubmittedEvent:
                _metrics.RecordOrderStatus(OrderStatus.Submitted);
                break;
            case OrderFilledEvent:
                _metrics.RecordOrderStatus(OrderStatus.Filled);
                break;
            case OrderRejectedEvent:
                _metrics.RecordOrderStatus(OrderStatus.Rejected);
                break;
            case OrderFailedEvent:
                _metrics.RecordOrderStatus(OrderStatus.Failed);
                break;
            case TradeExecutionCompletedEvent completed:
                _metrics.RecordExecution(success: completed.Success, completed.Duration.TotalMilliseconds);
                break;
        }

        // 3. Persistent Auditing (Section 11 / Workflow Audit)
        try
        {
            string opName = @event.GetType().Name.Replace("Event", "");
            string entityId = @event.OrderId?.ToString() ?? @event.SignalId.ToString();
            string entityType = @event.OrderId != null ? "Order" : "Signal";
            string description = $"Status: {@event.Status}. Duration: {@event.Duration.TotalMilliseconds}ms. ExecutionId: {@event.ExecutionId}. SignalId: {@event.SignalId}";

            if (@event is OrderFilledEvent filledEvent)
            {
                description += $". Executed Price: {filledEvent.ExecutedPrice}, Executed Qty: {filledEvent.ExecutedQuantity}";
            }
            else if (@event is OrderRejectedEvent rejectedEvent)
            {
                description += $". Reason: {rejectedEvent.Reason}";
            }
            else if (@event is OrderFailedEvent failedEvent)
            {
                description += $". Reason: {failedEvent.Reason}";
            }

            var auditLog = SystemLog.CreateAuditLog("INFO", opName, entityType, entityId, description);
            await _systemLogRepository.AddAsync(auditLog, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist audit log for event {EventType}", @event.GetType().Name);
        }
    }
}
