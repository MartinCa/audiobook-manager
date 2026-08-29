using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueGenreNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing databases can already hold duplicate genre rows: until now nothing stopped
            // two concurrent "get or create" calls from both inserting the same name, and the
            // index below would fail outright on those. Collapse each set of same-named rows onto
            // its lowest id first, moving the book links across.
            //
            // Order matters: the join rows have to stop referencing the duplicates before the
            // duplicates are deleted, and a book already linked to the surviving genre must have
            // its second link dropped rather than repointed - that would collide with the
            // (books_id, genres_id) primary key.
            migrationBuilder.Sql("""
                DELETE FROM audiobook_genre
                WHERE genres_id NOT IN (SELECT MIN(id) FROM genres GROUP BY name)
                  AND EXISTS (
                      SELECT 1 FROM audiobook_genre AS surviving
                      WHERE surviving.books_id = audiobook_genre.books_id
                        AND surviving.genres_id = (
                            SELECT MIN(keep.id) FROM genres AS keep
                            WHERE keep.name = (
                                SELECT dup.name FROM genres AS dup WHERE dup.id = audiobook_genre.genres_id)));
                """);

            migrationBuilder.Sql("""
                UPDATE audiobook_genre
                SET genres_id = (
                    SELECT MIN(keep.id) FROM genres AS keep
                    WHERE keep.name = (
                        SELECT dup.name FROM genres AS dup WHERE dup.id = audiobook_genre.genres_id))
                WHERE genres_id NOT IN (SELECT MIN(id) FROM genres GROUP BY name);
                """);

            migrationBuilder.Sql("""
                DELETE FROM genres WHERE id NOT IN (SELECT MIN(id) FROM genres GROUP BY name);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_genres_name",
                table: "genres",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_genres_name",
                table: "genres");
        }
    }
}
