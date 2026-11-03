using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticsService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                table: "ExperimentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "ExperimentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExperimentType",
                table: "ExperimentRuns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "tool");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "ExperimentRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservationsJson",
                table: "ExperimentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualitativeNotes",
                table: "ExperimentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultsJson",
                table: "ExperimentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScenarioKey",
                table: "ExperimentRuns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ExperimentRuns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.CreateTable(
                name: "ScenarioFindings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Experiment = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ResearchQuestion = table.Column<string>(type: "text", nullable: false),
                    Requirements = table.Column<string>(type: "text", nullable: false),
                    Hypothesis = table.Column<string>(type: "text", nullable: false),
                    ExpectedProperties = table.Column<string>(type: "text", nullable: false),
                    Observations = table.Column<string>(type: "text", nullable: false),
                    Advantages = table.Column<string>(type: "text", nullable: false),
                    Disadvantages = table.Column<string>(type: "text", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    Limitations = table.Column<string>(type: "text", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioFindings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TradeOffNotes",
                columns: table => new
                {
                    Experiment = table.Column<string>(type: "text", nullable: false),
                    Criterion = table.Column<string>(type: "text", nullable: false),
                    Approach = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    EvidenceType = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOffNotes", x => new { x.Experiment, x.Criterion, x.Approach });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentRuns_ExperimentType_CreatedAt",
                table: "ExperimentRuns",
                columns: new[] { "ExperimentType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentRuns_GroupId",
                table: "ExperimentRuns",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScenarioFindings");

            migrationBuilder.DropTable(
                name: "TradeOffNotes");

            migrationBuilder.DropIndex(
                name: "IX_ExperimentRuns_ExperimentType_CreatedAt",
                table: "ExperimentRuns");

            migrationBuilder.DropIndex(
                name: "IX_ExperimentRuns_GroupId",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "DetailsJson",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "ExperimentType",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "ObservationsJson",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "QualitativeNotes",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "ResultsJson",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "ScenarioKey",
                table: "ExperimentRuns");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ExperimentRuns");
        }
    }
}
