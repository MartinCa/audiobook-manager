using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audiobook_series",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    audiobook_id = table.Column<long>(type: "INTEGER", nullable: false),
                    series_name = table.Column<string>(type: "TEXT", nullable: false),
                    series_part = table.Column<string>(type: "TEXT", nullable: true),
                    is_primary = table.Column<bool>(type: "INTEGER", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false),
                    series_name_folded = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiobook_series", x => x.id);
                    table.ForeignKey(
                        name: "fk_audiobook_series_audiobooks_audiobook_id",
                        column: x => x.audiobook_id,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audiobook_series_audiobook_id",
                table: "audiobook_series",
                column: "audiobook_id",
                unique: true,
                filter: "is_primary = 1");

            migrationBuilder.CreateIndex(
                name: "ix_audiobook_series_audiobook_id_series_name",
                table: "audiobook_series",
                columns: new[] { "audiobook_id", "series_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiobook_series_series_name",
                table: "audiobook_series",
                column: "series_name");

            // Every existing book with a series gets its one relation, the primary. fold_accents is
            // the per-connection SQLite function AccentFoldingConnectionInterceptor registers, so
            // the folded column starts out exactly as the save interceptor would have written it.
            //
            // Known exception to the mirror invariant: a row whose series is whitespace-only is
            // skipped (it has no usable name to relate to), so such a legacy book keeps that value
            // in audiobooks.series with no relation row. Normalizing legacy series values is the
            // follow-up tracked in #1564; nothing reads the blank value as a series meanwhile.
            migrationBuilder.Sql(
                """
                INSERT INTO audiobook_series (audiobook_id, series_name, series_part, is_primary, sort_order, series_name_folded)
                SELECT id, TRIM(series), NULLIF(TRIM(series_part), ''), 1, 0, fold_accents(TRIM(series))
                FROM audiobooks
                WHERE series IS NOT NULL AND TRIM(series) <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audiobook_series");
        }
    }
}
