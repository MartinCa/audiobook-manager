using System;
using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedExpectedBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expected_books",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source_name = table.Column<string>(type: "TEXT", nullable: false),
                    source_book_id = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    year = table.Column<int>(type: "INTEGER", nullable: true),
                    release_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    source_url = table.Column<string>(type: "TEXT", nullable: true),
                    image_url = table.Column<string>(type: "TEXT", nullable: true),
                    series_id = table.Column<long>(type: "INTEGER", nullable: true),
                    source_series_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_series_name = table.Column<string>(type: "TEXT", nullable: true),
                    series_position = table.Column<string>(type: "TEXT", nullable: true),
                    is_compilation = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_ignored = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    first_seen_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_refreshed_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expected_books", x => x.id);
                    table.ForeignKey(
                        name: "fk_expected_books_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expected_book_authors",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    expected_book_id = table.Column<long>(type: "INTEGER", nullable: false),
                    person_id = table.Column<long>(type: "INTEGER", nullable: true),
                    author_name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expected_book_authors", x => x.id);
                    table.ForeignKey(
                        name: "fk_expected_book_authors_expected_books_expected_book_id",
                        column: x => x.expected_book_id,
                        principalTable: "expected_books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expected_book_authors_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        // SetNull, not Cascade: the link keeps its AuthorName when the Person row
                        // goes away, so the roster stays readable and a later refresh can
                        // re-resolve the person (see ExpectedBookAuthorMapping).
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expected_book_authors_expected_book_id",
                table: "expected_book_authors",
                column: "expected_book_id");

            migrationBuilder.CreateIndex(
                name: "ix_expected_book_authors_person_id",
                table: "expected_book_authors",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_expected_books_series_id",
                table: "expected_books",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_expected_books_source_name_source_book_id",
                table: "expected_books",
                columns: new[] { "source_name", "source_book_id" },
                unique: true,
                filter: "source_book_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expected_books_source_series_id",
                table: "expected_books",
                column: "source_series_id");

            // --- Data copy from the two legacy roster tables (this migration is additive) ---
            //
            // Every legacy row is given a unique synthetic source_book_id
            // (ExpectedBook.LegacySeriesSyntheticPrefix / LegacyAuthorSyntheticPrefix + the legacy
            // row id) so the partial unique index on (source_name, source_book_id) holds and each
            // copied row is uniquely addressable. A later phase's refresh adopts a legacy row by
            // natural key (series link + title, or author link + title) and overwrites the
            // synthetic id with the real source book id; the legacy TABLES themselves are not
            // touched by this migration - the follow-up DropLegacyExpectedBookTables migration
            // drops them once every deployed database runs the chain. Until then the two sources
            // of truth coexist and report the same books. The prefix namespace is reserved - a
            // real source id must never start with it, or adoption could pair it with the wrong
            // row (see ExpectedBook.IsLegacySyntheticSourceBookId).

            migrationBuilder.Sql($"""
                INSERT INTO expected_books (
                    source_name, source_book_id, title, year, release_date, source_url,
                    series_id, source_series_id, source_series_name, series_position,
                    is_compilation, is_ignored, first_seen_at, last_refreshed_at)
                SELECT
                    COALESCE(s.matched_source_name, 'Hardcover'),
                    '{ExpectedBook.LegacySeriesSyntheticPrefix}' || seb.id,
                    seb.title,
                    seb.year,
                    seb.release_date,
                    seb.source_url,
                    seb.series_id,
                    s.matched_source_id,
                    s.matched_series_name,
                    seb.position,
                    seb.is_compilation,
                    seb.is_ignored,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                FROM series_expected_books seb
                INNER JOIN series s ON s.id = seb.series_id;
                """);

            migrationBuilder.Sql($"""
                INSERT INTO expected_books (
                    source_name, source_book_id, title, year, release_date, source_url,
                    is_compilation, is_ignored, first_seen_at, last_refreshed_at)
                SELECT
                    COALESCE(p.matched_source_name, 'Hardcover'),
                    '{ExpectedBook.LegacyAuthorSyntheticPrefix}' || aeb.id,
                    aeb.title,
                    aeb.year,
                    aeb.release_date,
                    aeb.source_url,
                    0,
                    aeb.is_ignored,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                FROM author_expected_books aeb
                INNER JOIN persons p ON p.id = aeb.person_id;
                """);

            // Author-attributed books always carry an author link (author_name from the person
            // row, person_id when the person exists). Correlated on the synthetic source_book_id
            // just written, which is unique per legacy author roster row.
            migrationBuilder.Sql($"""
                INSERT INTO expected_book_authors (expected_book_id, person_id, author_name)
                SELECT eb.id, aeb.person_id, p.name
                FROM author_expected_books aeb
                INNER JOIN persons p ON p.id = aeb.person_id
                INNER JOIN expected_books eb ON eb.source_book_id = '{ExpectedBook.LegacyAuthorSyntheticPrefix}' || aeb.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the two new tables are dropped. The legacy roster tables are untouched by
            // this migration, so rolling back loses just the copied rows - the legacy data it
            // was copied from remains and the app keeps working against the old tables.
            migrationBuilder.DropTable(
                name: "expected_book_authors");

            migrationBuilder.DropTable(
                name: "expected_books");
        }
    }
}
