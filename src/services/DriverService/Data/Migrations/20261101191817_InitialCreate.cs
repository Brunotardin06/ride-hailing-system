using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DriverService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClosedRides",
                columns: table => new
                {
                    RideId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosedRides", x => x.RideId);
                });

            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Vehicle = table.Column<string>(type: "text", nullable: false),
                    LicensePlate = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AvailableSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentRideId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedRides = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RideId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassengerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassengerName = table.Column<string>(type: "text", nullable: false),
                    Pickup = table.Column<string>(type: "text", nullable: false),
                    Destination = table.Column<string>(type: "text", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Destination = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Envelope = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessage",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessage", x => x.EventId);
                });

            migrationBuilder.InsertData(
                table: "Drivers",
                columns: new[] { "Id", "AvailableSince", "CompletedRides", "CurrentRideId", "LicensePlate", "Name", "Rating", "Status", "Vehicle" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, "GX-123-B", "Emma de Vries", 4.9000000000000004, "Available", "Toyota Prius · white" },
                    { new Guid("11111111-1111-1111-1111-000000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 8, 1, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, null, "TS-482-K", "Lucas Janssen", 4.7999999999999998, "Available", "Tesla Model 3 · black" },
                    { new Guid("11111111-1111-1111-1111-000000000003"), null, 0, null, "ZK-907-L", "Sophie Bakker", 4.7000000000000002, "Offline", "Skoda Octavia · grey" },
                    { new Guid("11111111-1111-1111-1111-000000000004"), null, 0, null, "HN-551-T", "Daan Visser", 4.5999999999999996, "Offline", "Kia e-Niro · blue" },
                    { new Guid("11111111-1111-1111-1111-000000000005"), null, 0, null, "PR-318-D", "Noor El Amrani", 4.9000000000000004, "Offline", "Volkswagen ID.4 · red" },
                    { new Guid("11111111-1111-1111-1111-000000000006"), null, 0, null, "VB-640-S", "Milan Peeters", 5.0, "Offline", "Mercedes E-Class · black" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Offers_DriverId_Status",
                table: "Offers",
                columns: new[] { "DriverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Offers_RequestKey",
                table: "Offers",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offers_RideId",
                table: "Offers",
                column: "RideId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_DispatchedAt",
                table: "OutboxMessage",
                column: "DispatchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosedRides");

            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "Offers");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "ProcessedMessage");
        }
    }
}
