using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertsAndAlertEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AlertType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Component = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TriggerCount = table.Column<int>(type: "integer", nullable: false),
                    NotificationCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EmailRecipient = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ExportFormat = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertEvents_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FailedMessageAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: false),
                    Component = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedMessageAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FailedMessageAnalyses_TelegramMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "TelegramMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageProcessingTrackers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageProcessingTrackers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageProcessingTrackers_TelegramMessages_TelegramMessageId",
                        column: x => x.TelegramMessageId,
                        principalTable: "TelegramMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Side",
                table: "Trades",
                column: "Side");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Symbol",
                table: "Trades",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_AlertId",
                table: "AlertEvents",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_CreatedAt",
                table: "AlertEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_EventType",
                table: "AlertEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CorrelationId",
                table: "Alerts",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_DeduplicationKey",
                table: "Alerts",
                column: "DeduplicationKey",
                unique: true,
                filter: "\"Status\" != 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_LastSeenAt",
                table: "Alerts",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_RuleId",
                table: "Alerts",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Status",
                table: "Alerts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TriggeredAt",
                table: "Alerts",
                column: "TriggeredAt");

            migrationBuilder.CreateIndex(
                name: "IX_FailedMessageAnalyses_MessageId",
                table: "FailedMessageAnalyses",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_FailedMessageAnalyses_Status",
                table: "FailedMessageAnalyses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MessageProcessingTrackers_State",
                table: "MessageProcessingTrackers",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_MessageProcessingTrackers_TelegramMessageId",
                table: "MessageProcessingTrackers",
                column: "TelegramMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchedules_IsActive",
                table: "ReportSchedules",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_Side",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Trades_Symbol",
                table: "Trades");

            migrationBuilder.DropTable(
                name: "AlertEvents");

            migrationBuilder.DropTable(
                name: "FailedMessageAnalyses");

            migrationBuilder.DropTable(
                name: "MessageProcessingTrackers");

            migrationBuilder.DropTable(
                name: "ReportSchedules");

            migrationBuilder.DropTable(
                name: "Alerts");
        }
    }
}
