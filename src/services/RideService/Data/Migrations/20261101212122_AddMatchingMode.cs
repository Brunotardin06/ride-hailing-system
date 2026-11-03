using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RideService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExperimentRunId",
                table: "Rides",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchingMode",
                table: "Rides",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OfferCreatedAt",
                table: "Rides",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferedDriverName",
                table: "Rides",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rides_ExperimentRunId",
                table: "Rides",
                column: "ExperimentRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rides_ExperimentRunId",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "ExperimentRunId",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "MatchingMode",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "OfferCreatedAt",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "OfferedDriverName",
                table: "Rides");
        }
    }
}
