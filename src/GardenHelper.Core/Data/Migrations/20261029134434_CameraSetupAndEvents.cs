using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraSetupAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "Cameras",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "KeepDays",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MotionEnabled",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MotionSensitivity",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotEveryMinutes",
                table: "Cameras",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CameraEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: true),
                    File = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CameraEvents_CameraId_AtUtc",
                table: "CameraEvents",
                columns: new[] { "CameraId", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CameraEvents");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "KeepDays",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "MotionEnabled",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "MotionSensitivity",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "SnapshotEveryMinutes",
                table: "Cameras");
        }
    }
}
