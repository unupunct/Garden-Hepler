using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteAndDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClimateJson",
                table: "Gardens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Gardens",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Gardens",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SatelliteCmPerPixel",
                table: "Gardens",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatelliteFile",
                table: "Gardens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Beds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GardenId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    ShadePercent = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plantings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BedId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlantId = table.Column<string>(type: "TEXT", nullable: false),
                    Variety = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    PlantedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plantings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plantings_Beds_BedId",
                        column: x => x.BedId,
                        principalTable: "Beds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Beds_GardenId",
                table: "Beds",
                column: "GardenId");

            migrationBuilder.CreateIndex(
                name: "IX_Plantings_BedId_Status",
                table: "Plantings",
                columns: new[] { "BedId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Plantings");

            migrationBuilder.DropTable(
                name: "Beds");

            migrationBuilder.DropColumn(
                name: "ClimateJson",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "SatelliteCmPerPixel",
                table: "Gardens");

            migrationBuilder.DropColumn(
                name: "SatelliteFile",
                table: "Gardens");
        }
    }
}
