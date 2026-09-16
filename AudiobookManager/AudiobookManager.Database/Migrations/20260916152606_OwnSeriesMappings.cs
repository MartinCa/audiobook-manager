using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class OwnSeriesMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // INTENTIONAL: every existing mapping row is deleted before the schema changes, and
            // this is a deliberate data loss, not a byproduct. The old series_mapping rows were
            // global regex -> free-text-target rules maintained from the Settings page; the new
            // model owns each pattern by a Series row (the target is always the owning series'
            // name). A global row cannot be safely re-assigned to an owner: its target string
            // need not match any existing catalog series value (or even a value in the library),
            // so guessing an owner would silently move mappings to the wrong series and
            // re-route books during metadata searches. Clearing the table is the only honest
            // migration; anyone who relied on a global pattern re-enters it on the series detail
            // page's management section after upgrading. Clearing first also keeps the
            // series_id NOT NULL column below satisfiable under SQLite (a NOT NULL column can be
            // added to an empty table only).
            migrationBuilder.Sql(
                "DELETE FROM series_mapping;");

            migrationBuilder.DropColumn(
                name: "mapped_series",
                table: "series_mapping");

            migrationBuilder.AddColumn<long>(
                name: "series_id",
                table: "series_mapping",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_series_mapping_series_id",
                table: "series_mapping",
                column: "series_id");

            migrationBuilder.AddForeignKey(
                name: "fk_series_mapping_series_series_id",
                table: "series_mapping",
                column: "series_id",
                principalTable: "series",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_series_mapping_series_series_id",
                table: "series_mapping");

            migrationBuilder.DropIndex(
                name: "ix_series_mapping_series_id",
                table: "series_mapping");

            // The old schema's mapped_series column is what the new model dropped: it held the
            // free-text target, which the owning series_id now encodes relationally. Restoring
            // the column must therefore copy each row's owner NAME back into mapped_series -
            // re-adding it as "" for every row would rewrite every retained mapping's target to
            // an empty series and silently stop routing the books it used to. The owner name is
            // resolved via the series row and folded into mapped_series BEFORE series_id is
            // dropped; a mapping whose owner no longer exists (cascaded away, which the FK that
            // just came off once enforced) falls back to "" rather than inserting a NULL into a
            // NOT NULL column.
            migrationBuilder.AddColumn<string>(
                name: "mapped_series",
                table: "series_mapping",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE series_mapping SET mapped_series = COALESCE(" +
                "(SELECT s.name FROM series s WHERE s.id = series_mapping.series_id), '')");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "series_mapping");
        }
    }
}
