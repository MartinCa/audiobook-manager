using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAccentFoldedSearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_folded",
                table: "persons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "book_name_folded",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_folded",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "series_folded",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitle_folded",
                table: "audiobooks",
                type: "TEXT",
                nullable: true);

            // Backfill for every row that already existed before this migration - AddColumn only
            // sets up the schema, it does not populate the new columns for existing rows, and
            // AccentFoldedColumnsInterceptor only keeps them in sync going forward (on the next
            // insert or update).
            //
            // fold_accents is the same SQL scalar function the app registers on every connection
            // (AccentFoldingConnectionInterceptor, wired up in DatabaseContext.OnConfiguring), and
            // this UPDATE depends on that having already happened on whatever connection actually
            // runs this migration. That holds for every path this codebase exercises -
            // DatabaseContext's parameterless constructor and `dotnet ef` both go through
            // OnConfiguring, and AddDbContext<DatabaseContext>() in DependencyInjection.cs is
            // called with no options action, so production startup's Database.Migrate() does too
            // (see AddAccentFoldedSearchColumnsMigration_BackfillsExistingRows for a real,
            // migrator-driven regression test of this exact backfill, not just a manual check). A
            // DbContext handed fully-external, already-configured DbContextOptions (skipping
            // OnConfiguring entirely) would not register the function and this UPDATE would fail
            // with "no such function: fold_accents" - nothing in this codebase does that, but it's
            // the assumption to preserve if that ever changes.
            migrationBuilder.Sql("UPDATE persons SET name_folded = fold_accents(name);");
            migrationBuilder.Sql(
                "UPDATE audiobooks SET " +
                "book_name_folded = fold_accents(book_name), " +
                "subtitle_folded = fold_accents(subtitle), " +
                "series_folded = fold_accents(series), " +
                "description_folded = fold_accents(description);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name_folded",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "book_name_folded",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "description_folded",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "series_folded",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "subtitle_folded",
                table: "audiobooks");
        }
    }
}
