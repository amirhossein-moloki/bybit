using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Worker;

public static class TelegramEndpoints
{
    public static void MapTelegramEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telegram")
                       .RequireAuthorization("DashboardRead");

        // Centralized Exception & Correlation ID Filter for all Telegram API endpoints
        group.AddEndpointFilter(async (context, next) =>
        {
            var correlationId = context.HttpContext.TraceIdentifier;
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            try
            {
                return await next(context);
            }
            catch (KeyNotFoundException ex)
            {
                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("TelegramEndpoints");
                logger?.LogWarning(ex, "Telegram API resource not found [CorrelationId: {CorrelationId}]", correlationId);
                return Results.Json(new { status = "error", code = "NotFound", message = ex.Message, correlationId }, statusCode: 404);
            }
            catch (ArgumentException ex)
            {
                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("TelegramEndpoints");
                logger?.LogWarning(ex, "Telegram API bad request [CorrelationId: {CorrelationId}]", correlationId);
                return Results.Json(new { status = "error", code = "BadRequest", message = ex.Message, correlationId }, statusCode: 400);
            }
            catch (InvalidOperationException ex)
            {
                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("TelegramEndpoints");
                logger?.LogWarning(ex, "Telegram API invalid operation [CorrelationId: {CorrelationId}]", correlationId);
                return Results.Json(new { status = "error", code = "InvalidOperation", message = ex.Message, correlationId }, statusCode: 400);
            }
            catch (Exception ex)
            {
                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("TelegramEndpoints");
                logger?.LogError(ex, "Telegram API unhandled error [CorrelationId: {CorrelationId}]", correlationId);
                return Results.Json(new { status = "error", code = "InternalServerError", message = "An internal server error occurred: " + ex.Message, correlationId }, statusCode: 500);
            }
        });

        // ----------------------------------------------------------------------
        // Authentication & Client Status Endpoints
        // ----------------------------------------------------------------------

        // 1. Connection Status
        group.MapGet("/status", async (ITelegramQrAuthService authService, CancellationToken ct) =>
        {
            var status = await authService.GetStatusAsync(ct);
            return Results.Ok(new { status = "success", data = status });
        });

        // 2. Start QR Auth
        group.MapPost("/auth/qr/start", async (ITelegramQrAuthService authService, CancellationToken ct) =>
        {
            var result = await authService.StartQrAuthAsync(ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 3. Get QR Auth Status
        group.MapGet("/auth/qr/status", async (ITelegramQrAuthService authService, string? sessionId, CancellationToken ct) =>
        {
            var status = await authService.GetQrStatusAsync(sessionId, ct);
            return Results.Ok(new { status = "success", data = status });
        });

        // 4. Start OTP Login
        group.MapPost("/auth/otp/start", async (ITelegramAuthenticationService authService, OtpStartRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.PhoneNumber))
            {
                return Results.BadRequest(new { success = false, error = "PhoneNumber is required." });
            }

            var result = await authService.StartOtpLoginAsync(request.PhoneNumber, ct);
            if (!result.Success)
            {
                return Results.BadRequest(result);
            }

            return Results.Ok(result);
        });

        // 5. Verify OTP Login
        group.MapPost("/auth/otp/verify", async (ITelegramAuthenticationService authService, OtpVerifyRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.PhoneNumber) || string.IsNullOrWhiteSpace(request?.Code))
            {
                return Results.BadRequest(new { success = false, error = "PhoneNumber and Code are required." });
            }

            var result = await authService.VerifyOtpAsync(request.PhoneNumber, request.PhoneCodeHash, request.Code, ct);
            if (!result.Success)
            {
                return Results.BadRequest(result);
            }

            return Results.Ok(result);
        });

        // 6. Verify 2FA Password
        group.MapPost("/auth/password", async (ITelegramAuthenticationService authService, PasswordVerifyRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Password))
            {
                return Results.BadRequest(new { success = false, error = "Password is required." });
            }

            var result = await authService.VerifyPasswordAsync(request.Password, ct);
            if (!result.Success)
            {
                return Results.BadRequest(result);
            }

            return Results.Ok(result);
        });

        // 7. Logout
        group.MapPost("/auth/logout", async (ITelegramQrAuthService authService, CancellationToken ct) =>
        {
            await authService.LogoutAsync(ct);
            return Results.Ok(new { status = "success", data = new { message = "Logged out successfully" } });
        });

        // 8. Get Dialogs (Channels and Groups)
        group.MapGet("/dialogs", async (ITelegramClient telegramClient) =>
        {
            var dialogs = await telegramClient.GetDialogsAsync();
            return Results.Ok(new { status = "success", data = dialogs });
        });

        // 9. Get Monitored Channels (Backward compatibility)
        group.MapGet("/channels", (ITelegramClient telegramClient) =>
        {
            var channels = telegramClient.GetMonitoredChannels();
            return Results.Ok(new { status = "success", data = channels });
        });

        // 10. Toggle Monitored Channel (Backward compatibility)
        group.MapPost("/channels/toggle", (ITelegramClient telegramClient, ToggleChannelRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Identifier))
            {
                return Results.BadRequest(new { status = "error", code = "InvalidSourceConfiguration", message = "Channel identifier is required." });
            }

            var success = telegramClient.ToggleMonitoredChannel(request.Identifier, request.Enable);
            return Results.Ok(new { status = "success", data = new { identifier = request.Identifier, enabled = request.Enable } });
        });

        // ----------------------------------------------------------------------
        // Telegram Control Center — Source Management Endpoints
        // ----------------------------------------------------------------------

        // 11. List Sources
        group.MapGet("/sources", async (
            ITelegramSourceService sourceService,
            string? search,
            string? type,
            bool? isEnabled,
            bool? listenForSignals,
            string? status,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var filter = new TelegramSourceFilterDto(
                search,
                type,
                isEnabled,
                listenForSignals,
                status,
                page ?? 1,
                pageSize ?? 20
            );

            var sources = await sourceService.GetSourcesAsync(filter, ct);
            return Results.Ok(new { status = "success", data = sources });
        });

        // 12. Sync Sources
        group.MapPost("/sources/sync", async (ITelegramSourceService sourceService, CancellationToken ct) =>
        {
            var result = await sourceService.SyncSourcesAsync(ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 13. Bulk Update Sources
        group.MapPost("/sources/bulk", async (ITelegramSourceService sourceService, BulkUpdateSourcesDto request, CancellationToken ct) =>
        {
            if (request == null || request.SourceIds == null || request.SourceIds.Count == 0)
            {
                return Results.BadRequest(new { status = "error", code = "InvalidSourceConfiguration", message = "At least one source ID must be provided." });
            }

            var updatedCount = await sourceService.BulkUpdateSourcesAsync(request, ct);
            return Results.Ok(new { status = "success", data = new { updatedCount } });
        });

        // 14. Get Single Source
        group.MapGet("/sources/{id:guid}", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var source = await sourceService.GetSourceByIdAsync(id, ct);
            if (source == null)
            {
                return Results.NotFound(new { status = "error", code = "SourceNotFound", message = $"Source with ID '{id}' was not found." });
            }

            return Results.Ok(new { status = "success", data = source });
        });

        // 15. Update Source Capabilities / Pause
        group.MapPatch("/sources/{id:guid}", async (ITelegramSourceService sourceService, Guid id, UpdateTelegramSourceDto request, CancellationToken ct) =>
        {
            var updated = await sourceService.UpdateSourceAsync(id, request, ct);
            return Results.Ok(new { status = "success", data = updated });
        });

        // 16. Delete Source
        group.MapDelete("/sources/{id:guid}", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var deleted = await sourceService.DeleteSourceAsync(id, ct);
            if (!deleted)
            {
                return Results.NotFound(new { status = "error", code = "SourceNotFound", message = $"Source with ID '{id}' was not found." });
            }

            return Results.Ok(new { status = "success", data = new { message = "Source deleted successfully." } });
        });

        // 17. Get Source Recent Messages
        group.MapGet("/sources/{id:guid}/messages", async (
            ITelegramSourceService sourceService,
            Guid id,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var messages = await sourceService.GetSourceMessagesAsync(id, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(new { status = "success", data = messages });
        });

        // 18. Get Source Detected Signals
        group.MapGet("/sources/{id:guid}/signals", async (
            ITelegramSourceService sourceService,
            Guid id,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var signals = await sourceService.GetSourceSignalsAsync(id, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(new { status = "success", data = signals });
        });

        // 19. Get Source Health
        group.MapGet("/sources/{id:guid}/health", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var health = await sourceService.GetSourceHealthAsync(id, ct);
            return Results.Ok(new { status = "success", data = health });
        });

        // 20. Test Source
        group.MapPost("/sources/{id:guid}/test", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var result = await sourceService.TestSourceAsync(id, ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 21. Live Message Processing Pipeline
        group.MapGet("/live-pipeline", async (
            ITelegramSourceService sourceService,
            Guid? sourceId,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var pipeline = await sourceService.GetLiveMessagePipelineAsync(sourceId, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(new { status = "success", data = pipeline });
        });

        // 22. Pipeline Diagnostics
        group.MapGet("/diagnostics", async (ITelegramSourceService sourceService, CancellationToken ct) =>
        {
            var diagnostics = await sourceService.GetPipelineDiagnosticsAsync(ct);
            return Results.Ok(new { status = "success", data = diagnostics });
        });

        // ----------------------------------------------------------------------
        // Telegram Message Reprocessing / Replay Endpoints
        // ----------------------------------------------------------------------

        // 23. Manual Reprocess Telegram Message
        group.MapPost("/messages/{messageId:guid}/reprocess", async (
            IMessageReprocessingService reprocessingService,
            Guid messageId,
            ReprocessMessageRequestDto? request,
            CancellationToken ct) =>
        {
            var result = await reprocessingService.ReprocessMessageAsync(messageId, request ?? new ReprocessMessageRequestDto(), ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 24. Get Processing Attempts History for Telegram Message
        group.MapGet("/messages/{messageId:guid}/attempts", async (
            IMessageReprocessingService reprocessingService,
            Guid messageId,
            CancellationToken ct) =>
        {
            var attempts = await reprocessingService.GetMessageAttemptsAsync(messageId, ct);
            return Results.Ok(new { status = "success", data = attempts });
        });

        // 25. Explicit Trade Execution for Completed Reprocessing Attempt
        group.MapPost("/messages/{messageId:guid}/attempts/{attemptId:guid}/execute", async (
            IMessageReprocessingService reprocessingService,
            Guid messageId,
            Guid attemptId,
            ExplicitExecutionRequestDto? request,
            CancellationToken ct) =>
        {
            var result = await reprocessingService.ExecuteAttemptTradeAsync(messageId, attemptId, request ?? new ExplicitExecutionRequestDto(attemptId), ct);
            return Results.Ok(new { status = "success", data = result });
        });
    }
}

public class ToggleChannelRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("enable")]
    public bool Enable { get; set; }
}
