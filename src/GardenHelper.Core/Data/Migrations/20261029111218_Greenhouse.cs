using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Greenhouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GreenhouseActuators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Desired = table.Column<bool>(type: "INTEGER", nullable: false),
                    Auto = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GreenhouseActuators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GreenhouseItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GreenhouseItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GreenhouseReadings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Metric = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GreenhouseReadings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GreenhouseSetups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    System = table.Column<string>(type: "TEXT", nullable: false),
                    TempMax = table.Column<double>(type: "REAL", nullable: false),
                    TempMin = table.Column<double>(type: "REAL", nullable: false),
                    HumidityMax = table.Column<double>(type: "REAL", nullable: false),
                    SoilMoistureMin = table.Column<double>(type: "REAL", nullable: false),
                    WaterTempMax = table.Column<double>(type: "REAL", nullable: false),
                    PhMin = table.Column<double>(type: "REAL", nullable: false),
                    PhMax = table.Column<double>(type: "REAL", nullable: false),
                    AutoControl = table.Column<bool>(type: "INTEGER", nullable: false),
                    WidthCm = table.Column<double>(type: "REAL", nullable: false),
                    LengthCm = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GreenhouseSetups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GreenhouseActuators_FeatureId_Key",
                table: "GreenhouseActuators",
                columns: new[] { "FeatureId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GreenhouseItems_FeatureId",
                table: "GreenhouseItems",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_GreenhouseReadings_FeatureId_Metric_AtUtc",
                table: "GreenhouseReadings",
                columns: new[] { "FeatureId", "Metric", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GreenhouseSetups_FeatureId",
                table: "GreenhouseSetups",
                column: "FeatureId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GreenhouseActuators");

            migrationBuilder.DropTable(
                name: "GreenhouseItems");

            migrationBuilder.DropTable(
                name: "GreenhouseReadings");

            migrationBuilder.DropTable(
                name: "GreenhouseSetups");
        }
    }
}
