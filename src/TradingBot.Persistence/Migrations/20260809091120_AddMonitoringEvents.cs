using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthCheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthCheckResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Component = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    OperationId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SignalId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExceptionType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalEventId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExchangeOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_CheckedAt",
                table: "HealthCheckResults",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_ServiceName",
                table: "HealthCheckResults",
                column: "ServiceName");

            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_Status",
                table: "HealthCheckResults",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_CorrelationId",
                table: "MonitoringEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_EventType",
                table: "MonitoringEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_OrderId",
                table: "MonitoringEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_PositionId",
                table: "MonitoringEvents",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_Severity",
                table: "MonitoringEvents",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_Source",
                table: "MonitoringEvents",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_Source_ExternalEventId",
                table: "MonitoringEvents",
                columns: new[] { "Source", "ExternalEventId" },
                unique: true,
                filter: "\"ExternalEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_Timestamp",
                table: "MonitoringEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_EventId",
                table: "ProcessedEvents",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealthCheckResults");

            migrationBuilder.DropTable(
                name: "MonitoringEvents");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");
        }
    }
}
