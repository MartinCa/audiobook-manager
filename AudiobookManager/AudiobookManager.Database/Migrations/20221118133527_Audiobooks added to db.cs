using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class Audiobooksaddedtodb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_series_mapping",
                table: "series_mapping");

            migrationBuilder.RenameIndex(
                name: "IX_series_mapping_regex",
                table: "series_mapping",
                newName: "ix_series_mapping_regex");

            migrationBuilder.AddPrimaryKey(
                name: "pk_series_mapping",
                table: "series_mapping",
                column: "id");

            migrationBuilder.CreateTable(
                name: "audiobooks",
                columns: table => new {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    bookname = table.Column<string>(name: "book_name", type: "TEXT", nullable: false),
                    subtitle = table.Column<string>(type: "TEXT", nullable: true),
                    series = table.Column<string>(type: "TEXT", nullable: true),
                    seriespart = table.Column<string>(name: "series_part", type: "TEXT", nullable: true),
                    year = table.Column<int>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    copyright = table.Column<string>(type: "TEXT", nullable: true),
                    publisher = table.Column<string>(type: "TEXT", nullable: true),
                    rating = table.Column<string>(type: "TEXT", nullable: true),
                    asin = table.Column<string>(type: "TEXT", nullable: true),
                    www = table.Column<string>(type: "TEXT", nullable: true),
                    coverfilepath = table.Column<string>(name: "cover_file_path", type: "TEXT", nullable: true),
                    durationinseconds = table.Column<int>(name: "duration_in_seconds", type: "INTEGER", nullable: true),
                    fileinfofullpath = table.Column<string>(name: "file_info_full_path", type: "TEXT", nullable: false),
                    fileinfofilename = table.Column<string>(name: "file_info_file_name", type: "TEXT", nullable: false),
                    fileinfosizeinbytes = table.Column<long>(name: "file_info_size_in_bytes", type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiobooks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                columns: table => new {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audiobook_genre",
                columns: table => new {
                    booksid = table.Column<long>(name: "books_id", type: "INTEGER", nullable: false),
                    genresid = table.Column<long>(name: "genres_id", type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiobook_genre", x => new { x.booksid, x.genresid });
                    table.ForeignKey(
                        name: "fk_audiobook_genre_audiobooks_books_id",
                        column: x => x.booksid,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_audiobook_genre_genres_genres_id",
                        column: x => x.genresid,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audiobooks_authors_persons",
                columns: table => new {
                    authorsid = table.Column<long>(name: "authors_id", type: "INTEGER", nullable: false),
                    booksauthoredid = table.Column<long>(name: "books_authored_id", type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiobooks_authors_persons", x => new { x.authorsid, x.booksauthoredid });
                    table.ForeignKey(
                        name: "fk_audiobooks_authors_persons_audiobooks_books_authored_id",
                        column: x => x.booksauthoredid,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_audiobooks_authors_persons_persons_authors_id",
                        column: x => x.authorsid,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audiobooks_narrators_persons",
                columns: table => new {
                    booksnarratedid = table.Column<long>(name: "books_narrated_id", type: "INTEGER", nullable: false),
                    narratorsid = table.Column<long>(name: "narrators_id", type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiobooks_narrators_persons", x => new { x.booksnarratedid, x.narratorsid });
                    table.ForeignKey(
                        name: "fk_audiobooks_narrators_persons_audiobooks_books_narrated_id",
                        column: x => x.booksnarratedid,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_audiobooks_narrators_persons_persons_narrators_id",
                        column: x => x.narratorsid,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audiobook_genre_genres_id",
                table: "audiobook_genre",
                column: "genres_id");

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_authors_persons_books_authored_id",
                table: "audiobooks_authors_persons",
                column: "books_authored_id");

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_narrators_persons_narrators_id",
                table: "audiobooks_narrators_persons",
                column: "narrators_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_name",
                table: "persons",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audiobook_genre");

            migrationBuilder.DropTable(
                name: "audiobooks_authors_persons");

            migrationBuilder.DropTable(
                name: "audiobooks_narrators_persons");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "audiobooks");

            migrationBuilder.DropTable(
                name: "persons");

            migrationBuilder.DropPrimaryKey(
                name: "pk_series_mapping",
                table: "series_mapping");

            migrationBuilder.RenameIndex(
                name: "ix_series_mapping_regex",
                table: "series_mapping",
                newName: "IX_series_mapping_regex");

            migrationBuilder.AddPrimaryKey(
                name: "PK_series_mapping",
                table: "series_mapping",
                column: "id");
        }
    }
}
