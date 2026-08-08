using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStopLossHistoryAndPositionTargetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangeOrderId",
                table: "PositionTargets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExecutedQuantity",
                table: "PositionTargets",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StopLossHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    NewPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopLossHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StopLossHistories_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StopLossHistories_PositionId",
                table: "StopLossHistories",
                column: "PositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StopLossHistories");

            migrationBuilder.DropColumn(
                name: "ExchangeOrderId",
                table: "PositionTargets");

            migrationBuilder.DropColumn(
                name: "ExecutedQuantity",
                table: "PositionTargets");
        }
    }
}
