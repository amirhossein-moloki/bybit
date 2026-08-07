using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeDecisionsAndRiskExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutedRules",
                table: "RiskEvaluations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "ExecutionTime",
                table: "RiskEvaluations",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<string>(
                name: "FailedRules",
                table: "RiskEvaluations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PassedRules",
                table: "RiskEvaluations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "RiskEvaluations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RiskProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaxRiskPerTrade = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    MaxDailyLoss = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    MaxWeeklyLoss = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    MaxMonthlyLoss = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    MaxOpenPositions = table.Column<int>(type: "integer", nullable: false),
                    MaxLeverage = table.Column<int>(type: "integer", nullable: false),
                    MaxExposure = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    MinimumRiskReward = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SignalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RiskEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDecisions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskProfiles");

            migrationBuilder.DropTable(
                name: "TradeDecisions");

            migrationBuilder.DropColumn(
                name: "ExecutedRules",
                table: "RiskEvaluations");

            migrationBuilder.DropColumn(
                name: "ExecutionTime",
                table: "RiskEvaluations");

            migrationBuilder.DropColumn(
                name: "FailedRules",
                table: "RiskEvaluations");

            migrationBuilder.DropColumn(
                name: "PassedRules",
                table: "RiskEvaluations");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "RiskEvaluations");
        }
    }
}
