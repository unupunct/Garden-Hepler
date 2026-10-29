using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapBackdrop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BackdropCmPerPixel",
                table: "Gardens",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackdropFile",
                table: "Gardens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BackdropTopLat",
                table: "Gardens",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BackdropTopLon",
                table: "Gardens",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackdropCmPerPixel",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "BackdropFile",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "BackdropTopLat",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "BackdropTopLon",
                table: "Gardens");
        }
    }
}
