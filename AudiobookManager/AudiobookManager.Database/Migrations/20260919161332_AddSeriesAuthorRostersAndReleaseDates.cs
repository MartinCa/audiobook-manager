using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesAuthorRostersAndReleaseDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "release_date",
                table: "series_expected_books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_refreshed_at",
                table: "persons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "author_expected_books",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    person_id = table.Column<long>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    year = table.Column<int>(type: "INTEGER", nullable: true),
                    release_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    source_url = table.Column<string>(type: "TEXT", nullable: true),
                    is_ignored = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_author_expected_books_person_id",
                table: "author_expected_books",
                column: "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "author_expected_books");

            migrationBuilder.DropColumn(
                name: "release_date",
                table: "series_expected_books");

            migrationBuilder.DropColumn(
                name: "last_refreshed_at",
                table: "persons");
        }
    }
}
