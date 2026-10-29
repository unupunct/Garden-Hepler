using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Harvests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Harvests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PlantId = table.Column<string>(type: "TEXT", nullable: false),
                    BedId = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantity = table.Column<double>(type: "REAL", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Harvests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_Date",
                table: "Harvests",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Harvests");
        }
    }
}
