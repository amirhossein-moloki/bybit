using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingBot.Persistence.Context;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TradingDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260812000000_AddSignalExtractions")]
    public partial class AddSignalExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignalExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    StopLoss = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    TakeProfitData = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalExtractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignalExtractions_TelegramMessages_TelegramMessageId",
                        column: x => x.TelegramMessageId,
                        principalTable: "TelegramMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.CheckConstraint("CK_SignalExtractions_Confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignalExtractions_TelegramMessageId",
                table: "SignalExtractions",
                column: "TelegramMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SignalExtractions_MessageId",
                table: "SignalExtractions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SignalExtractions_Symbol",
                table: "SignalExtractions",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_SignalExtractions_Status",
                table: "SignalExtractions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignalExtractions");
        }
    }
}
