using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Worker;

public static class TelegramEndpoints
{
    public static void MapTelegramEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telegram")
                       .RequireAuthorization("DashboardRead");

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
    }
}
