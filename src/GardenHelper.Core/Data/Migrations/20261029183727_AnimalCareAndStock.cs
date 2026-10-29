using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnimalCareAndStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "StockDate",
                table: "FeedRations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StockKg",
                table: "FeedRations",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "AnimalLogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockDate",
                table: "FeedRations");

            migrationBuilder.DropColumn(
                name: "StockKg",
                table: "FeedRations");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "AnimalLogs");
        }
    }
}
