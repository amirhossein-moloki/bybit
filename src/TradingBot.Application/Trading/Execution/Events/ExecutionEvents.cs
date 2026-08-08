using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Events;

public interface IExecutionEvent
{
    Guid ExecutionId { get; }
    Guid? OrderId { get; }
    Guid SignalId { get; }
    string Symbol { get; }
    string Status { get; }
    TimeSpan Duration { get; }
    DateTime Timestamp { get; }
}

public record TradeExecutionStartedEvent(
    Guid ExecutionId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp
) : IExecutionEvent
{
    public Guid? OrderId => null;
    string IExecutionEvent.Status => Status.ToString();
}

public record OrderSubmissionStartedEvent(
    Guid ExecutionId,
    Guid? OrderId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp
) : IExecutionEvent
{
    string IExecutionEvent.Status => Status.ToString();
}

public record OrderSubmittedEvent(
    Guid ExecutionId,
    Guid? OrderId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp
) : IExecutionEvent
{
    string IExecutionEvent.Status => Status.ToString();
}

public record OrderFilledEvent(
    Guid ExecutionId,
    Guid? OrderId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp,
    decimal ExecutedPrice,
    decimal ExecutedQuantity
) : IExecutionEvent
{
    string IExecutionEvent.Status => Status.ToString();
}

public record OrderRejectedEvent(
    Guid ExecutionId,
    Guid? OrderId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp,
    string Reason
) : IExecutionEvent
{
    string IExecutionEvent.Status => Status.ToString();
}

public record OrderFailedEvent(
    Guid ExecutionId,
    Guid? OrderId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp,
    string Reason
) : IExecutionEvent
{
    string IExecutionEvent.Status => Status.ToString();
}

public record TradeExecutionCompletedEvent(
    Guid ExecutionId,
    Guid? OrderId,
    Guid SignalId,
    string Symbol,
    OrderStatus Status,
    TimeSpan Duration,
    DateTime Timestamp,
    bool Success
) : IExecutionEvent
{
    string IExecutionEvent.Status => Status.ToString();
}
