using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class PropertyBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoundaryJson",
                table: "Gardens",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundaryJson",
                table: "Gardens");
        }
    }
}
