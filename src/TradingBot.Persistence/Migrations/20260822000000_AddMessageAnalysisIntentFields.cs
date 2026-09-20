using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAnalysisIntentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Intent",
                table: "MessageAnalyses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "MessageAnalyses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetSymbol",
                table: "MessageAnalyses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingStatus",
                table: "MessageAnalyses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.AddColumn<string>(
                name: "ExtractedMetadata",
                table: "MessageAnalyses",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAnalyses_Intent",
                table: "MessageAnalyses",
                column: "Intent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessageAnalyses_Intent",
                table: "MessageAnalyses");

            migrationBuilder.DropColumn(
                name: "Intent",
                table: "MessageAnalyses");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "MessageAnalyses");

            migrationBuilder.DropColumn(
                name: "TargetSymbol",
                table: "MessageAnalyses");

            migrationBuilder.DropColumn(
                name: "ProcessingStatus",
                table: "MessageAnalyses");

            migrationBuilder.DropColumn(
                name: "ExtractedMetadata",
                table: "MessageAnalyses");
        }
    }
}
