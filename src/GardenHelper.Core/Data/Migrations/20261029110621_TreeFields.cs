using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class TreeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BedId",
                table: "Inventory",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CrownRadiusCm",
                table: "Inventory",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeightCm",
                table: "Inventory",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTree",
                table: "Inventory",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlantedOn",
                table: "Inventory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RootRadiusCm",
                table: "Inventory",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TreeType",
                table: "Inventory",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BedId",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "CrownRadiusCm",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "IsTree",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "PlantedOn",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "RootRadiusCm",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "TreeType",
                table: "Inventory");
        }
    }
}
