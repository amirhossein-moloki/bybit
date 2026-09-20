using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramMessageReplyAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReplyToMessageId",
                table: "TelegramMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaInfo",
                table: "TelegramMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditInfo",
                table: "TelegramMessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessages_ReplyToMessageId",
                table: "TelegramMessages",
                column: "ReplyToMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelegramMessages_ReplyToMessageId",
                table: "TelegramMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "TelegramMessages");

            migrationBuilder.DropColumn(
                name: "MediaInfo",
                table: "TelegramMessages");

            migrationBuilder.DropColumn(
                name: "EditInfo",
                table: "TelegramMessages");
        }
    }
}
