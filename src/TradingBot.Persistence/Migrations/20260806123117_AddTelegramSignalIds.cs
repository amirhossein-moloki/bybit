using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramSignalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TelegramChannelId",
                table: "Signals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TelegramMessageId",
                table: "Signals",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ClosedAt",
                table: "Trades",
                column: "ClosedAt");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trades_Price",
                table: "Trades",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trades_Quantity",
                table: "Trades",
                sql: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_TelegramChannelId_TelegramMessageId",
                table: "Signals",
                columns: new[] { "TelegramChannelId", "TelegramMessageId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Signals_EntryPrice",
                table: "Signals",
                sql: "\"EntryPrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Signals_Price",
                table: "Signals",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Signals_Quantity",
                table: "Signals",
                sql: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Status",
                table: "Positions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Symbol",
                table: "Positions",
                column: "Symbol");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Positions_EntryPrice",
                table: "Positions",
                sql: "\"EntryPrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Positions_Quantity",
                table: "Positions",
                sql: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Symbol",
                table: "Orders",
                column: "Symbol");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Price",
                table: "Orders",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Quantity",
                table: "Orders",
                sql: "\"Quantity\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_ClosedAt",
                table: "Trades");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Trades_Price",
                table: "Trades");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Trades_Quantity",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Signals_TelegramChannelId_TelegramMessageId",
                table: "Signals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Signals_EntryPrice",
                table: "Signals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Signals_Price",
                table: "Signals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Signals_Quantity",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_Positions_Status",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_Symbol",
                table: "Positions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Positions_EntryPrice",
                table: "Positions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Positions_Quantity",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Symbol",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Price",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Quantity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TelegramChannelId",
                table: "Signals");

            migrationBuilder.DropColumn(
                name: "TelegramMessageId",
                table: "Signals");
        }
    }
}
