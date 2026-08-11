using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;

namespace TradingBot.Worker;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/analytics")
                       .RequireAuthorization("DashboardRead");

        // 1. GET /api/analytics/performance
        group.MapGet("/performance", async (
            IPerformanceAnalyticsService service,
            string? startDate,
            string? endDate,
            string? symbol,
            CancellationToken ct) =>
        {
            var validation = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = validation.ErrorMessage } });
            }

            var query = new GetAnalyticsQuery(startUtc, endUtc, symbol);
            var result = await service.GetPerformanceMetricsAsync(query, ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 2. GET /api/analytics/drawdown
        group.MapGet("/drawdown", async (
            IPerformanceAnalyticsService service,
            string? startDate,
            string? endDate,
            string? symbol,
            decimal? initialBalance,
            CancellationToken ct) =>
        {
            var validation = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = validation.ErrorMessage } });
            }

            if (initialBalance.HasValue && initialBalance.Value <= 0)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Initial balance must be greater than zero." } });
            }

            var query = new GetAnalyticsQuery(startUtc, endUtc, symbol, initialBalance);
            var result = await service.GetDrawdownMetricsAsync(query, ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 3. GET /api/analytics/streaks
        group.MapGet("/streaks", async (
            IPerformanceAnalyticsService service,
            string? startDate,
            string? endDate,
            string? symbol,
            CancellationToken ct) =>
        {
            var validation = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = validation.ErrorMessage } });
            }

            var query = new GetAnalyticsQuery(startUtc, endUtc, symbol);
            var result = await service.GetStreakMetricsAsync(query, ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 4. GET /api/analytics/duration
        group.MapGet("/duration", async (
            IPerformanceAnalyticsService service,
            string? startDate,
            string? endDate,
            string? symbol,
            CancellationToken ct) =>
        {
            var validation = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = validation.ErrorMessage } });
            }

            var query = new GetAnalyticsQuery(startUtc, endUtc, symbol);
            var result = await service.GetDurationMetricsAsync(query, ct);
            return Results.Ok(new { status = "success", data = result });
        });

        // 5. GET /api/analytics/side-performance
        group.MapGet("/side-performance", async (
            IPerformanceAnalyticsService service,
            string? startDate,
            string? endDate,
            string? symbol,
            CancellationToken ct) =>
        {
            var validation = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = validation.ErrorMessage } });
            }

            var query = new GetAnalyticsQuery(startUtc, endUtc, symbol);
            var result = await service.GetLongShortPerformanceAsync(query, ct);
            return Results.Ok(new { status = "success", data = result });
        });
    }

    private static (bool IsValid, string? ErrorMessage) ParseAndValidateDates(
        string? startStr,
        string? endStr,
        out DateTime? startDate,
        out DateTime? endDate)
    {
        startDate = null;
        endDate = null;

        if (!string.IsNullOrEmpty(startStr))
        {
            if (!DateTime.TryParse(startStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var s))
            {
                return (false, "Invalid 'startDate' format.");
            }
            startDate = s.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : s.ToUniversalTime();
        }

        if (!string.IsNullOrEmpty(endStr))
        {
            if (!DateTime.TryParse(endStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var e))
            {
                return (false, "Invalid 'endDate' format.");
            }
            endDate = e.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : e.ToUniversalTime();
        }

        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            return (false, "The 'startDate' must be less than or equal to the 'endDate'.");
        }

        return (true, null);
    }
}
