using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriverService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DestinationLat",
                table: "Offers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestinationLng",
                table: "Offers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PickupLat",
                table: "Offers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PickupLng",
                table: "Offers",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationLat",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "DestinationLng",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "PickupLat",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "PickupLng",
                table: "Offers");
        }
    }
}
