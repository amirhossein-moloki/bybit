using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangePositionId",
                table: "Positions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDesynchronized",
                table: "Positions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Fee",
                table: "Positions",
                type: "numeric(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "Positions",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Margin",
                table: "Positions",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedPnL",
                table: "Positions",
                type: "numeric(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingQuantity",
                table: "Positions",
                type: "numeric(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PositionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionEvents_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PositionTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetNumber = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionTargets", x => x.Id);
                    table.CheckConstraint("CK_PositionTargets_Percentage", "\"Percentage\" > 0 AND \"Percentage\" <= 100");
                    table.CheckConstraint("CK_PositionTargets_Price", "\"Price\" > 0");
                    table.CheckConstraint("CK_PositionTargets_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_PositionTargets_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ExchangePositionId",
                table: "Positions",
                column: "ExchangePositionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Positions_CurrentPrice",
                table: "Positions",
                sql: "\"CurrentPrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Positions_RemainingQuantity",
                table: "Positions",
                sql: "\"RemainingQuantity\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PositionEvents_EventType",
                table: "PositionEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_PositionEvents_PositionId",
                table: "PositionEvents",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionTargets_PositionId",
                table: "PositionTargets",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionTargets_PositionId_TargetNumber",
                table: "PositionTargets",
                columns: new[] { "PositionId", "TargetNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionEvents");

            migrationBuilder.DropTable(
                name: "PositionTargets");

            migrationBuilder.DropIndex(
                name: "IX_Positions_ExchangePositionId",
                table: "Positions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Positions_CurrentPrice",
                table: "Positions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Positions_RemainingQuantity",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExchangePositionId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "IsDesynchronized",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Fee",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Margin",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "RealizedPnL",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "RemainingQuantity",
                table: "Positions");
        }
    }
}
