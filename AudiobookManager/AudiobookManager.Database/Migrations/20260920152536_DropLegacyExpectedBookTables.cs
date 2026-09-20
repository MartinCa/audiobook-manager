using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyExpectedBookTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "author_expected_books");

            migrationBuilder.DropTable(
                name: "series_expected_books");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema-only recreation: SQLite cannot restore the dropped rows (the data had
            // already been copied into expected_books by AddUnifiedExpectedBooks, and a refresh
            // must re-adopt those rows), so a rollback leaves the legacy tables empty. This is a
            // documented tradeoff - the migration is irreversible in practice and is only
            // provided so a downgraded build can still start against an empty legacy schema
            // rather than failing on the missing tables.
            migrationBuilder.CreateTable(
                name: "author_expected_books",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    person_id = table.Column<long>(type: "INTEGER", nullable: false),
                    is_ignored = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    release_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    source_url = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    year = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_author_expected_books", x => x.id);
                    table.ForeignKey(
                        name: "fk_author_expected_books_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_expected_books",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    series_id = table.Column<long>(type: "INTEGER", nullable: false),
                    is_compilation = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_ignored = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    position = table.Column<string>(type: "TEXT", nullable: true),
                    release_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    source_url = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    year = table.Column<int>(type: "INTEGER", nullable: true)
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
                name: "ix_author_expected_books_person_id",
                table: "author_expected_books",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_series_expected_books_series_id",
                table: "series_expected_books",
                column: "series_id");
        }
    }
}
