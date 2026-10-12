using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeSeriesStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before the flat series/series_part/series_folded columns are dropped, audiobook_series has to
            // hold everything they did: names are put in canonical form (trimmed, blank = none, same name
            // ignoring case) and the two are made to agree. The flat columns win on a disagreement - they
            // are what the m4b tags and the library path were built from, so the file on disk already
            // says so. Parts are only trimmed here; they are put in canonical form ("Book 2" -> "2")
            // the next time a book is saved.
            // fold_accents is the per-connection function AccentFoldingConnectionInterceptor registers.

            // 1. The mirror: blank series is no series; otherwise trimmed.
            migrationBuilder.Sql(
                """
                UPDATE audiobooks SET series = NULL, series_part = NULL, series_folded = NULL
                WHERE series IS NOT NULL AND TRIM(series) = '';
                """);
            migrationBuilder.Sql(
                """
                UPDATE audiobooks
                SET series = TRIM(series), series_folded = fold_accents(TRIM(series))
                WHERE series IS NOT NULL AND series <> TRIM(series);
                """);
            migrationBuilder.Sql(
                """
                UPDATE audiobooks SET series_part = NULLIF(TRIM(series_part), '')
                WHERE series_part IS NOT NULL AND series_part <> TRIM(series_part) OR series_part = '';
                """);

            // 2. Relation rows without a usable name are not relations.
            migrationBuilder.Sql("DELETE FROM audiobook_series WHERE TRIM(series_name) = '';");

            // 3. The same series under different casing/padding collapses to one row: the primary
            //    wins, then the lowest id. (Done before trimming names, which would otherwise be able
            //    to collide with the ordinal unique index.)
            migrationBuilder.Sql(
                """
                DELETE FROM audiobook_series
                WHERE EXISTS (
                    SELECT 1 FROM audiobook_series other
                    WHERE other.audiobook_id = audiobook_series.audiobook_id
                      AND other.id <> audiobook_series.id
                      AND TRIM(other.series_name) = TRIM(audiobook_series.series_name) COLLATE NOCASE
                      AND (other.is_primary > audiobook_series.is_primary
                           OR (other.is_primary = audiobook_series.is_primary AND other.id < audiobook_series.id)));
                """);

            // 4. Where the book has a mirrored series, a primary row naming a different series is
            //    stale, and so is an additional row that duplicates the mirror's series. A book whose
            //    mirror is empty but which has a primary row takes that row's series instead (there
            //    is nothing the file could have been built from to prefer).
            migrationBuilder.Sql(
                """
                DELETE FROM audiobook_series
                WHERE EXISTS (SELECT 1 FROM audiobooks a WHERE a.id = audiobook_series.audiobook_id AND a.series IS NOT NULL)
                  AND ((is_primary = 1 AND NOT EXISTS (
                        SELECT 1 FROM audiobooks a
                        WHERE a.id = audiobook_series.audiobook_id AND a.series = TRIM(audiobook_series.series_name)))
                    OR (is_primary = 0 AND EXISTS (
                        SELECT 1 FROM audiobooks a
                        WHERE a.id = audiobook_series.audiobook_id
                          AND a.series = TRIM(audiobook_series.series_name) COLLATE NOCASE)));
                """);
            migrationBuilder.Sql(
                """
                UPDATE audiobooks
                SET series = (SELECT TRIM(r.series_name) FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1),
                    series_part = (SELECT NULLIF(TRIM(r.series_part), '') FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1),
                    series_folded = (SELECT fold_accents(TRIM(r.series_name)) FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1)
                WHERE series IS NULL
                  AND EXISTS (SELECT 1 FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1);
                """);

            // 5. Trim what is left, then put the primary's part in step with the mirror.
            migrationBuilder.Sql(
                """
                UPDATE audiobook_series
                SET series_name = TRIM(series_name), series_name_folded = fold_accents(TRIM(series_name))
                WHERE series_name <> TRIM(series_name);
                """);
            migrationBuilder.Sql(
                """
                UPDATE audiobook_series SET series_part = NULLIF(TRIM(series_part), '')
                WHERE series_part IS NOT NULL AND series_part <> TRIM(series_part) OR series_part = '';
                """);
            migrationBuilder.Sql(
                """
                UPDATE audiobook_series
                SET series_part = (SELECT a.series_part FROM audiobooks a WHERE a.id = audiobook_series.audiobook_id)
                WHERE is_primary = 1
                  AND series_part IS NOT (SELECT a.series_part FROM audiobooks a WHERE a.id = audiobook_series.audiobook_id);
                """);

            // 6. A book with a series and no primary row gets one, ahead of its other relations.
            migrationBuilder.Sql(
                """
                UPDATE audiobook_series SET sort_order = sort_order + 1
                WHERE is_primary = 0 AND audiobook_id IN (
                    SELECT a.id FROM audiobooks a
                    WHERE a.series IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM audiobook_series r WHERE r.audiobook_id = a.id AND r.is_primary = 1));
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO audiobook_series (audiobook_id, series_name, series_part, is_primary, sort_order, series_name_folded)
                SELECT a.id, a.series, a.series_part, 1, 0, fold_accents(a.series)
                FROM audiobooks a
                WHERE a.series IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM audiobook_series r WHERE r.audiobook_id = a.id AND r.is_primary = 1);
                """);

            migrationBuilder.DropIndex(
                name: "ix_audiobooks_series",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "series",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "series_folded",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "series_part",
                table: "audiobooks");

            migrationBuilder.CreateIndex(
                name: "ix_audiobook_series_series_name_folded_audiobook_id",
                table: "audiobook_series",
                columns: new[] { "series_name_folded", "audiobook_id" },
                filter: "is_primary = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audiobook_series_series_name_folded_audiobook_id",
                table: "audiobook_series");

            migrationBuilder.AddColumn<string>(
                name: "series",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_folded",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_part",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            // The columns come back as a copy of each book's primary relation.
            migrationBuilder.Sql(
                """
                UPDATE audiobooks
                SET series = (SELECT r.series_name FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1),
                    series_part = (SELECT r.series_part FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1),
                    series_folded = (SELECT r.series_name_folded FROM audiobook_series r WHERE r.audiobook_id = audiobooks.id AND r.is_primary = 1);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_series",
                table: "audiobooks",
                column: "series");
        }
    }
}
