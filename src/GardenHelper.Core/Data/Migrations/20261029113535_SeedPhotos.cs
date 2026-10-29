using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackPhoto",
                table: "Seeds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrontPhoto",
                table: "Seeds",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackPhoto",
                table: "Seeds");

            migrationBuilder.DropColumn(
                name: "FrontPhoto",
                table: "Seeds");
        }
    }
}
