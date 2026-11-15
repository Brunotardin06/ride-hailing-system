using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RideService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRideCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DestinationLat",
                table: "Rides",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestinationLng",
                table: "Rides",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PickupLat",
                table: "Rides",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PickupLng",
                table: "Rides",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationLat",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "DestinationLng",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "PickupLat",
                table: "Rides");

            migrationBuilder.DropColumn(
                name: "PickupLng",
                table: "Rides");
        }
    }
}
