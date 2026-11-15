using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriverService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferRequestKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestKey",
                table: "Offers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Hand-written: rows created before this migration would all get "" and break the UNIQUE index,
            // so give each existing offer a unique key (its own Id).
            migrationBuilder.Sql("UPDATE \"Offers\" SET \"RequestKey\" = \"Id\"::text WHERE \"RequestKey\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_RequestKey",
                table: "Offers",
                column: "RequestKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Offers_RequestKey",
                table: "Offers");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "Offers");
        }
    }
}
