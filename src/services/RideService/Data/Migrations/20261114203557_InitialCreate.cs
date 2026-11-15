using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RideService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Rides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassengerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassengerName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Pickup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverName = table.Column<string>(type: "text", nullable: true),
                    Vehicle = table.Column<string>(type: "text", nullable: true),
                    LicensePlate = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusReason = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rides_DriverId_Status",
                table: "Rides",
                columns: new[] { "DriverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Rides_PassengerId_RequestedAt",
                table: "Rides",
                columns: new[] { "PassengerId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rides");
        }
    }
}
