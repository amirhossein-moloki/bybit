using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParserVersion",
                table: "Signals",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedAt",
                table: "Signals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationMessage",
                table: "Signals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationStatus",
                table: "Signals",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "Signals");

            migrationBuilder.DropColumn(
                name: "ValidatedAt",
                table: "Signals");

            migrationBuilder.DropColumn(
                name: "ValidationMessage",
                table: "Signals");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                table: "Signals");
        }
    }
}
