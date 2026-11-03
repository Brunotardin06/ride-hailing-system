using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalyticsService.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixToolRunDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Runs created before AddScenarioExperiments got an empty type/status; they are supplementary tool runs.
            migrationBuilder.Sql("UPDATE \"ExperimentRuns\" SET \"ExperimentType\" = 'tool' WHERE \"ExperimentType\" = '';");
            migrationBuilder.Sql("UPDATE \"ExperimentRuns\" SET \"Status\" = 'Completed' WHERE \"Status\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
