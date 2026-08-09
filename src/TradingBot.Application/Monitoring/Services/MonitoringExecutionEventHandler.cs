using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Events;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class MonitoringExecutionEventHandler : IExecutionEventHandler
{
    private readonly IMonitoringEventPublisher _publisher;

    public MonitoringExecutionEventHandler(IMonitoringEventPublisher publisher)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public async Task HandleAsync(IExecutionEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) return;

        // Map internal events to standardized MonitoringEvent (Section 38 & 40)
        string eventType = @event.GetType().Name.Replace("Event", "");
        string severity = "INFORMATION";
        string status = "Succeeded";
        string? errorCode = null;
        string? payload = null;

        // Extract specialized information based on concrete event types
        switch (@event)
        {
            case TradeExecutionStartedEvent:
                eventType = "SignalAccepted";
                status = "Started";
                break;
            case OrderSubmissionStartedEvent:
                eventType = "OrderCreated";
                status = "Started";
                break;
            case OrderSubmittedEvent:
                eventType = "OrderSubmitted";
                status = "Succeeded";
                break;
            case OrderFilledEvent filled:
                eventType = "OrderFilled";
                status = "Succeeded";
                payload = $"{{\"ExecutedPrice\": {filled.ExecutedPrice}, \"ExecutedQuantity\": {filled.ExecutedQuantity}}}";
                break;
            case OrderRejectedEvent rejected:
                eventType = "OrderRejected";
                severity = "ERROR";
                status = "Rejected";
                errorCode = "ORDER_REJECTED";
                payload = $"{{\"Reason\": \"{rejected.Reason}\"}}";
                break;
            case OrderFailedEvent failed:
                eventType = "OrderFailed";
                severity = "ERROR";
                status = "Failed";
                errorCode = "ORDER_FAILED";
                payload = $"{{\"Reason\": \"{failed.Reason}\"}}";
                break;
            case TradeExecutionCompletedEvent completed:
                eventType = "OrderAccepted"; // Completed successfully
                status = completed.Success ? "Succeeded" : "Failed";
                severity = completed.Success ? "INFORMATION" : "ERROR";
                break;
        }

        var message = $"Execution event '{eventType}' occurred for Symbol {@event.Symbol}. Status: {@event.Status}. Duration: {@event.Duration.TotalMilliseconds}ms.";

        var monitoringEvent = new MonitoringEvent(
            eventType,
            severity,
            "ExecutionEngine",
            "TradeExecutionOrchestrator",
            status,
            message,
            correlationId: @event.ExecutionId.ToString(), // Standardized CorrelationId (Section 14 & 68)
            signalId: @event.SignalId,
            orderId: @event.OrderId,
            payload: payload,
            errorCode: errorCode,
            timestamp: @event.Timestamp
        );

        await _publisher.PublishAsync(monitoringEvent, forceSynchronous: false, cancellationToken: cancellationToken);
    }
}
