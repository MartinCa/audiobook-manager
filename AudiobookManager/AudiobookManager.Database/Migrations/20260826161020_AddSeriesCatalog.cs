using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "series",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    matched_source_name = table.Column<string>(type: "TEXT", nullable: true),
                    matched_source_id = table.Column<string>(type: "TEXT", nullable: true),
                    matched_source_url = table.Column<string>(type: "TEXT", nullable: true),
                    matched_series_name = table.Column<string>(type: "TEXT", nullable: true),
                    match_confidence = table.Column<double>(type: "REAL", nullable: true),
                    last_refreshed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "series_expected_books",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    series_id = table.Column<long>(type: "INTEGER", nullable: false),
                    position = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    year = table.Column<int>(type: "INTEGER", nullable: true),
                    source_url = table.Column<string>(type: "TEXT", nullable: true),
                    is_ignored = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series_expected_books", x => x.id);
                    table.ForeignKey(
                        name: "fk_series_expected_books_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_series_name",
                table: "series",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_expected_books_series_id",
                table: "series_expected_books",
                column: "series_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_expected_books");

            migrationBuilder.DropTable(
                name: "series");
        }
    }
}
