using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DriverService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentDriverPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExperiment",
                table: "Offers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsExperimentDriver",
                table: "Drivers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-000000000001"),
                column: "IsExperimentDriver",
                value: false);

            migrationBuilder.UpdateData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-000000000002"),
                column: "IsExperimentDriver",
                value: false);

            migrationBuilder.UpdateData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-000000000003"),
                column: "IsExperimentDriver",
                value: false);

            migrationBuilder.UpdateData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-000000000004"),
                column: "IsExperimentDriver",
                value: false);

            migrationBuilder.UpdateData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-000000000005"),
                column: "IsExperimentDriver",
                value: false);

            migrationBuilder.UpdateData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-000000000006"),
                column: "IsExperimentDriver",
                value: false);

            migrationBuilder.InsertData(
                table: "Drivers",
                columns: new[] { "Id", "AvailableSince", "CompletedRides", "CurrentRideId", "IsExperimentDriver", "LicensePlate", "Name", "Rating", "Status", "Vehicle" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-100000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 1, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-01", "Experiment driver 01", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 2, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-02", "Experiment driver 02", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000003"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 3, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-03", "Experiment driver 03", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000004"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 4, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-04", "Experiment driver 04", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000005"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 5, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-05", "Experiment driver 05", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000006"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 6, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-06", "Experiment driver 06", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000007"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 7, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-07", "Experiment driver 07", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000008"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 8, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-08", "Experiment driver 08", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000009"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 9, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-09", "Experiment driver 09", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000010"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 10, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-10", "Experiment driver 10", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000011"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 11, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-11", "Experiment driver 11", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000012"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 12, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-12", "Experiment driver 12", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000013"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 13, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-13", "Experiment driver 13", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000014"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 14, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-14", "Experiment driver 14", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000015"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 15, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-15", "Experiment driver 15", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000016"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 16, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-16", "Experiment driver 16", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000017"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 17, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-17", "Experiment driver 17", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000018"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 18, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-18", "Experiment driver 18", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000019"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 19, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-19", "Experiment driver 19", 5.0, "Available", "Test vehicle" },
                    { new Guid("11111111-1111-1111-1111-100000000020"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 20, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, true, "EXP-20", "Experiment driver 20", 5.0, "Available", "Test vehicle" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000001"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000002"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000003"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000004"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000005"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000006"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000007"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000008"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000009"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000010"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000011"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000012"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000013"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000014"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000015"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000016"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000017"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000018"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000019"));

            migrationBuilder.DeleteData(
                table: "Drivers",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-100000000020"));

            migrationBuilder.DropColumn(
                name: "IsExperiment",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "IsExperimentDriver",
                table: "Drivers");
        }
    }
}
