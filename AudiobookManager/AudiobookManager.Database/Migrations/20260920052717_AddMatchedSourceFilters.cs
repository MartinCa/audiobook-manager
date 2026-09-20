using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchedSourceFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "hardcover_author_url",
                table: "persons",
                newName: "matched_source_url");

            migrationBuilder.RenameColumn(
                name: "hardcover_author_name",
                table: "persons",
                newName: "matched_source_name");

            migrationBuilder.RenameColumn(
                name: "hardcover_author_id",
                table: "persons",
                newName: "matched_source_id");

            migrationBuilder.AddColumn<string>(
                name: "matched_source_name",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            // Backfill for every row that already existed before this migration - AddColumn only
            // sets up the schema, and AccentFoldedColumnsInterceptor only keeps this column in
            // sync going forward (on the next insert or update). resolve_metadata_source is the
            // same SQLite scalar function the app registers on every connection
            // (AccentFoldingConnectionInterceptor, wired up in DatabaseContext.OnConfiguring) -
            // see AddAccentFoldedSearchColumns for the same backfill-via-registered-function
            // pattern and the assumption it depends on.
            migrationBuilder.Sql("UPDATE audiobooks SET matched_source_name = resolve_metadata_source(www);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "matched_source_name",
                table: "audiobooks");

            migrationBuilder.RenameColumn(
                name: "matched_source_url",
                table: "persons",
                newName: "hardcover_author_url");

            migrationBuilder.RenameColumn(
                name: "matched_source_name",
                table: "persons",
                newName: "hardcover_author_name");

            migrationBuilder.RenameColumn(
                name: "matched_source_id",
                table: "persons",
                newName: "hardcover_author_id");
        }
    }
}
