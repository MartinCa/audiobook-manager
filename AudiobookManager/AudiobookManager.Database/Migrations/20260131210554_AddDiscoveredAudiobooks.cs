using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveredAudiobooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovered_audiobooks",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    book_name = table.Column<string>(type: "TEXT", nullable: false),
                    subtitle = table.Column<string>(type: "TEXT", nullable: true),
                    series = table.Column<string>(type: "TEXT", nullable: true),
                    series_part = table.Column<string>(type: "TEXT", nullable: true),
                    year = table.Column<int>(type: "INTEGER", nullable: true),
                    authors = table.Column<string>(type: "TEXT", nullable: true),
                    narrators = table.Column<string>(type: "TEXT", nullable: true),
                    genres = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    copyright = table.Column<string>(type: "TEXT", nullable: true),
                    publisher = table.Column<string>(type: "TEXT", nullable: true),
                    rating = table.Column<string>(type: "TEXT", nullable: true),
                    asin = table.Column<string>(type: "TEXT", nullable: true),
                    www = table.Column<string>(type: "TEXT", nullable: true),
                    duration_in_seconds = table.Column<int>(type: "INTEGER", nullable: true),
                    file_info_full_path = table.Column<string>(type: "TEXT", nullable: false),
                    file_info_file_name = table.Column<string>(type: "TEXT", nullable: false),
                    file_info_size_in_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    discovered_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovered_audiobooks", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovered_audiobooks");
        }
    }
}
