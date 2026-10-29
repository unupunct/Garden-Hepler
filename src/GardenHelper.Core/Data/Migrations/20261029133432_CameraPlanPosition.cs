using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraPlanPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PlanX",
                table: "Cameras",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PlanY",
                table: "Cameras",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanX",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "PlanY",
                table: "Cameras");
        }
    }
}
