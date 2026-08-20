using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Worker;

public static class TelegramEndpoints
{
    public static void MapTelegramEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telegram")
                       .RequireAuthorization("DashboardRead");

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

        // 4. Logout
        group.MapPost("/auth/logout", async (ITelegramQrAuthService authService, CancellationToken ct) =>
        {
            await authService.LogoutAsync(ct);
            return Results.Ok(new { status = "success", data = new { message = "Logged out successfully" } });
        });

        // 5. Get Dialogs (Channels and Groups)
        group.MapGet("/dialogs", async (ITelegramClient telegramClient) =>
        {
            try
            {
                var dialogs = await telegramClient.GetDialogsAsync();
                return Results.Ok(new { status = "success", data = dialogs });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { status = "error", code = "TelegramUnavailable", message = ex.Message });
            }
        });

        // 6. Get Monitored Channels (Backward compatibility)
        group.MapGet("/channels", (ITelegramClient telegramClient) =>
        {
            var channels = telegramClient.GetMonitoredChannels();
            return Results.Ok(new { status = "success", data = channels });
        });

        // 7. Toggle Monitored Channel (Backward compatibility)
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

        // 8. List Sources
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

        // 9. Sync Sources
        group.MapPost("/sources/sync", async (ITelegramSourceService sourceService, CancellationToken ct) =>
        {
            try
            {
                var result = await sourceService.SyncSourcesAsync(ct);
                return Results.Ok(new { status = "success", data = result });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { status = "error", code = "SynchronizationFailed", message = "Sync failed: " + ex.Message });
            }
        });

        // 10. Bulk Update Sources
        group.MapPost("/sources/bulk", async (ITelegramSourceService sourceService, BulkUpdateSourcesDto request, CancellationToken ct) =>
        {
            if (request == null || request.SourceIds == null || request.SourceIds.Count == 0)
            {
                return Results.BadRequest(new { status = "error", code = "InvalidSourceConfiguration", message = "At least one source ID must be provided." });
            }

            var updatedCount = await sourceService.BulkUpdateSourcesAsync(request, ct);
            return Results.Ok(new { status = "success", data = new { updatedCount } });
        });

        // 11. Get Single Source
        group.MapGet("/sources/{id:guid}", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var source = await sourceService.GetSourceByIdAsync(id, ct);
            if (source == null)
            {
                return Results.NotFound(new { status = "error", code = "SourceNotFound", message = $"Source with ID '{id}' was not found." });
            }

            return Results.Ok(new { status = "success", data = source });
        });

        // 12. Update Source Capabilities / Pause
        group.MapPatch("/sources/{id:guid}", async (ITelegramSourceService sourceService, Guid id, UpdateTelegramSourceDto request, CancellationToken ct) =>
        {
            try
            {
                var updated = await sourceService.UpdateSourceAsync(id, request, ct);
                return Results.Ok(new { status = "success", data = updated });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { status = "error", code = "SourceNotFound", message = $"Source with ID '{id}' was not found." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { status = "error", code = "InvalidSourceConfiguration", message = ex.Message });
            }
        });

        // 13. Delete Source
        group.MapDelete("/sources/{id:guid}", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var deleted = await sourceService.DeleteSourceAsync(id, ct);
            if (!deleted)
            {
                return Results.NotFound(new { status = "error", code = "SourceNotFound", message = $"Source with ID '{id}' was not found." });
            }

            return Results.Ok(new { status = "success", data = new { message = "Source deleted successfully." } });
        });

        // 14. Get Source Recent Messages
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

        // 15. Get Source Detected Signals
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

        // 16. Get Source Health
        group.MapGet("/sources/{id:guid}/health", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            try
            {
                var health = await sourceService.GetSourceHealthAsync(id, ct);
                return Results.Ok(new { status = "success", data = health });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { status = "error", code = "SourceNotFound", message = $"Source with ID '{id}' was not found." });
            }
        });

        // 17. Test Source
        group.MapPost("/sources/{id:guid}/test", async (ITelegramSourceService sourceService, Guid id, CancellationToken ct) =>
        {
            var result = await sourceService.TestSourceAsync(id, ct);
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
