using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class MonitoringEventPublisher : IMonitoringEventPublisher
{
    private readonly IMonitoringEventQueue _queue;
    private readonly IEventSanitizer _sanitizer;
    private readonly MonitoringOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MonitoringEventPublisher> _logger;

    public MonitoringEventPublisher(
        IMonitoringEventQueue queue,
        IEventSanitizer sanitizer,
        MonitoringOptions options,
        IServiceProvider serviceProvider,
        ILogger<MonitoringEventPublisher> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(MonitoringEvent @event, bool forceSynchronous = false, CancellationToken cancellationToken = default)
    {
        if (!_options.Observability.Enabled) return;
        if (@event == null) return;

        // Sanitize and limit payload size (Section 11 & 12)
        var maxPayloadSize = _options.Observability.MaxPayloadSize;
        var sanitizedMessage = _sanitizer.Sanitize(@event.Message) ?? string.Empty;
        var sanitizedPayload = _sanitizer.SanitizeAndLimit(@event.Payload, maxPayloadSize);
        var sanitizedExceptionType = _sanitizer.Sanitize(@event.ExceptionType);
        var sanitizedErrorCode = _sanitizer.Sanitize(@event.ErrorCode);

        // Re-create sanitized event with a new ID if needed or preserve existing
        var sanitizedEvent = new MonitoringEvent(
            @event.EventType,
            @event.Severity,
            @event.Source,
            @event.Component,
            @event.Status,
            sanitizedMessage,
            correlationId: @event.CorrelationId,
            operationId: @event.OperationId,
            signalId: @event.SignalId,
            orderId: @event.OrderId,
            positionId: @event.PositionId,
            payload: sanitizedPayload,
            errorCode: sanitizedErrorCode,
            exceptionType: sanitizedExceptionType,
            externalEventId: @event.ExternalEventId,
            timestamp: @event.Timestamp
        );

        // Structured logging (Section 13)
        LogLevel logLevel = @event.Severity.ToUpperInvariant() switch
        {
            "CRITICAL" => LogLevel.Critical,
            "ERROR" => LogLevel.Error,
            "WARNING" => LogLevel.Warning,
            "INFORMATION" => LogLevel.Information,
            "DEBUG" => LogLevel.Debug,
            "TRACE" => LogLevel.Trace,
            _ => LogLevel.Information
        };

        if (_options.Observability.StructuredLogging)
        {
            _logger.Log(logLevel, "MonitoringEvent: {EventType} from {Source}. Status: {Status}. Msg: {Message}. CorrId: {CorrelationId}, OrderId: {OrderId}, PosId: {PositionId}",
                sanitizedEvent.EventType, sanitizedEvent.Source, sanitizedEvent.Status, sanitizedEvent.Message, sanitizedEvent.CorrelationId, sanitizedEvent.OrderId, sanitizedEvent.PositionId);
        }

        if (forceSynchronous)
        {
            if (_options.Observability.PersistenceEnabled)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IMonitoringEventRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    await repo.AddAsync(sanitizedEvent, cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    // Section 34 & 35: Prevent recursive loops, log safely
                    _logger.LogError(ex, "Failed to persist monitoring event synchronously.");
                }
            }
        }
        else
        {
            await _queue.EnqueueAsync(sanitizedEvent, cancellationToken);
        }
    }
}
