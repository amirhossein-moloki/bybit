using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageProcessingAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageProcessingAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TriggerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TriggeredBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProcessingMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ParserVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AiModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Intent = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EntryPrice = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    StopLoss = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    TakeProfitsJson = table.Column<string>(type: "text", nullable: true),
                    Leverage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SpreadAllowance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ValidationResult = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RiskResult = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExecutionResult = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageProcessingAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageProcessingAttempts_TelegramMessages_TelegramMessageId",
                        column: x => x.TelegramMessageId,
                        principalTable: "TelegramMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageProcessingAttempts_Status",
                table: "MessageProcessingAttempts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MessageProcessingAttempts_TelegramMessageId",
                table: "MessageProcessingAttempts",
                column: "TelegramMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageProcessingAttempts_TelegramMessageId_AttemptNumber",
                table: "MessageProcessingAttempts",
                columns: new[] { "TelegramMessageId", "AttemptNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageProcessingAttempts");
        }
    }
}
