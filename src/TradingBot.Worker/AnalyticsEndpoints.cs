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
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Worker;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/analytics")
                       .RequireAuthorization("DashboardRead");

        // Existing endpoints (1 to 5) are kept intact
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

        // New Endpoints for Phase 11 - Stage 04
        // 6. GET /api/analytics/report (Full performance report)
        group.MapGet("/report", async (
            IAnalyticsReportingService service,
            string? startDate,
            string? endDate,
            string? symbol,
            string? side,
            decimal? minPnL,
            decimal? maxPnL,
            string? closeReason,
            decimal? initialBalance,
            bool? bypassCache,
            CancellationToken ct) =>
        {
            var dateVal = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!dateVal.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = dateVal.ErrorMessage } });
            }

            OrderSide? sideEnum = null;
            if (!string.IsNullOrEmpty(side))
            {
                if (Enum.TryParse<OrderSide>(side, true, out var s))
                {
                    sideEnum = s;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'side' parameter." } });
                }
            }

            CloseReason? reasonEnum = null;
            if (!string.IsNullOrEmpty(closeReason))
            {
                if (Enum.TryParse<CloseReason>(closeReason, true, out var r))
                {
                    reasonEnum = r;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'closeReason' parameter." } });
                }
            }

            if (initialBalance.HasValue && initialBalance.Value <= 0)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Initial balance must be greater than zero." } });
            }

            var filters = new ReportFilterDto(startUtc, endUtc, symbol, sideEnum, minPnL, maxPnL, reasonEnum);
            var report = await service.GenerateReportAsync(filters, initialBalance, bypassCache ?? false, ct);
            return Results.Ok(new { status = "success", data = report });
        });

        // 7. GET /api/analytics/equity-curve
        group.MapGet("/equity-curve", async (
            IAnalyticsReportingService service,
            string? startDate,
            string? endDate,
            string? symbol,
            string? side,
            decimal? minPnL,
            decimal? maxPnL,
            string? closeReason,
            decimal? initialBalance,
            CancellationToken ct) =>
        {
            var dateVal = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!dateVal.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = dateVal.ErrorMessage } });
            }

            OrderSide? sideEnum = null;
            if (!string.IsNullOrEmpty(side))
            {
                if (Enum.TryParse<OrderSide>(side, true, out var s))
                {
                    sideEnum = s;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'side' parameter." } });
                }
            }

            CloseReason? reasonEnum = null;
            if (!string.IsNullOrEmpty(closeReason))
            {
                if (Enum.TryParse<CloseReason>(closeReason, true, out var r))
                {
                    reasonEnum = r;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'closeReason' parameter." } });
                }
            }

            if (initialBalance.HasValue && initialBalance.Value <= 0)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Initial balance must be greater than zero." } });
            }

            var filters = new ReportFilterDto(startUtc, endUtc, symbol, sideEnum, minPnL, maxPnL, reasonEnum);
            var points = await service.GetEquityCurveAsync(filters, initialBalance, ct);
            return Results.Ok(new { status = "success", data = points });
        });

        // 8. GET /api/analytics/aggregation
        group.MapGet("/aggregation", async (
            IAnalyticsReportingService service,
            string? startDate,
            string? endDate,
            string? symbol,
            string? side,
            decimal? minPnL,
            decimal? maxPnL,
            string? closeReason,
            string? period,
            CancellationToken ct) =>
        {
            var dateVal = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!dateVal.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = dateVal.ErrorMessage } });
            }

            OrderSide? sideEnum = null;
            if (!string.IsNullOrEmpty(side))
            {
                if (Enum.TryParse<OrderSide>(side, true, out var s))
                {
                    sideEnum = s;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'side' parameter." } });
                }
            }

            CloseReason? reasonEnum = null;
            if (!string.IsNullOrEmpty(closeReason))
            {
                if (Enum.TryParse<CloseReason>(closeReason, true, out var r))
                {
                    reasonEnum = r;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'closeReason' parameter." } });
                }
            }

            var aggregationPeriod = AggregationPeriod.Daily;
            if (!string.IsNullOrEmpty(period))
            {
                if (Enum.TryParse<AggregationPeriod>(period, true, out var p))
                {
                    aggregationPeriod = p;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'period' parameter (allowed: Daily, Weekly, Monthly)." } });
                }
            }

            var filters = new ReportFilterDto(startUtc, endUtc, symbol, sideEnum, minPnL, maxPnL, reasonEnum);
            var agg = await service.GetHistoricalAggregationAsync(filters, aggregationPeriod, ct);
            return Results.Ok(new { status = "success", data = agg });
        });

        // 9. GET /api/analytics/export/csv
        group.MapGet("/export/csv", async (
            IAnalyticsReportingService service,
            string? startDate,
            string? endDate,
            string? symbol,
            string? side,
            decimal? minPnL,
            decimal? maxPnL,
            string? closeReason,
            CancellationToken ct) =>
        {
            var dateVal = ParseAndValidateDates(startDate, endDate, out var startUtc, out var endUtc);
            if (!dateVal.IsValid)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = dateVal.ErrorMessage } });
            }

            OrderSide? sideEnum = null;
            if (!string.IsNullOrEmpty(side))
            {
                if (Enum.TryParse<OrderSide>(side, true, out var s))
                {
                    sideEnum = s;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'side' parameter." } });
                }
            }

            CloseReason? reasonEnum = null;
            if (!string.IsNullOrEmpty(closeReason))
            {
                if (Enum.TryParse<CloseReason>(closeReason, true, out var r))
                {
                    reasonEnum = r;
                }
                else
                {
                    return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "Invalid 'closeReason' parameter." } });
                }
            }

            var filters = new ReportFilterDto(startUtc, endUtc, symbol, sideEnum, minPnL, maxPnL, reasonEnum);
            var csv = await service.ExportTradesToCsvAsync(filters, ct);

            return Results.Content(csv, "text/csv");
        });

        // 10. POST /api/analytics/schedule
        group.MapPost("/schedule", async (
            IAnalyticsReportingService service,
            ReportScheduleDto scheduleDto,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleDto.ScheduleName))
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "ScheduleName is required." } });
            }

            if (string.IsNullOrWhiteSpace(scheduleDto.CronExpression))
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "CronExpression is required." } });
            }

            if (string.IsNullOrWhiteSpace(scheduleDto.EmailRecipient))
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = "EmailRecipient is required." } });
            }

            try
            {
                var saved = await service.SaveReportScheduleAsync(scheduleDto, ct);
                return Results.Ok(new { status = "success", data = saved });
            }
            catch (DomainException ex)
            {
                return Results.BadRequest(new { status = "error", error = new { code = "VALIDATION_FAILED", message = ex.Message } });
            }
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
