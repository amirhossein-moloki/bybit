using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingBot.Persistence.Context;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TradingDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260811000000_AddSignalIntelligence")]
    public partial class AddSignalIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    SenderId = table.Column<long>(type: "bigint", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Processed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    ExtractedData = table.Column<string>(type: "text", nullable: false),
                    AIUsed = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageAnalyses_TelegramMessages_TelegramMessageId",
                        column: x => x.TelegramMessageId,
                        principalTable: "TelegramMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.CheckConstraint("CK_MessageAnalyses_Confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 1");
                });

            migrationBuilder.CreateTable(
                name: "SignalContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SignalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastAction = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    LastMessageId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignalContexts_Signals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "Signals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessages_ChannelId_MessageId",
                table: "TelegramMessages",
                columns: new[] { "ChannelId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessages_Processed",
                table: "TelegramMessages",
                column: "Processed");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAnalyses_TelegramMessageId",
                table: "MessageAnalyses",
                column: "TelegramMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAnalyses_MessageType",
                table: "MessageAnalyses",
                column: "MessageType");

            migrationBuilder.CreateIndex(
                name: "IX_SignalContexts_SignalId",
                table: "SignalContexts",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_SignalContexts_ChannelId_Symbol",
                table: "SignalContexts",
                columns: new[] { "ChannelId", "Symbol" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignalContexts");

            migrationBuilder.DropTable(
                name: "MessageAnalyses");

            migrationBuilder.DropTable(
                name: "TelegramMessages");
        }
    }
}
