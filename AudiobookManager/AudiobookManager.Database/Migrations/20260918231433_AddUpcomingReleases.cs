using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUpcomingReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hardcover_author_id",
                table: "persons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hardcover_author_name",
                table: "persons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hardcover_author_url",
                table: "persons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "author_follows",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    person_id = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_author_follows", x => x.id);
                    table.ForeignKey(
                        name: "fk_author_follows_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_follows",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    series_id = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series_follows", x => x.id);
                    table.ForeignKey(
                        name: "fk_series_follows_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upcoming_releases",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    release_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    person_id = table.Column<long>(type: "INTEGER", nullable: true),
                    series_id = table.Column<long>(type: "INTEGER", nullable: true),
                    series_position = table.Column<string>(type: "TEXT", nullable: true),
                    source_name = table.Column<string>(type: "TEXT", nullable: false),
                    source_book_id = table.Column<string>(type: "TEXT", nullable: false),
                    source_url = table.Column<string>(type: "TEXT", nullable: true),
                    image_url = table.Column<string>(type: "TEXT", nullable: true),
                    discovered_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upcoming_releases", x => x.id);
                    table.ForeignKey(
                        name: "fk_upcoming_releases_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_upcoming_releases_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_author_follows_person_id",
                table: "author_follows",
                column: "person_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_follows_series_id",
                table: "series_follows",
                column: "series_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_upcoming_releases_person_id",
                table: "upcoming_releases",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_upcoming_releases_series_id",
                table: "upcoming_releases",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_upcoming_releases_source_name_source_book_id",
                table: "upcoming_releases",
                columns: new[] { "source_name", "source_book_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "author_follows");

            migrationBuilder.DropTable(
                name: "series_follows");

            migrationBuilder.DropTable(
                name: "upcoming_releases");

            migrationBuilder.DropColumn(
                name: "hardcover_author_id",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "hardcover_author_name",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "hardcover_author_url",
                table: "persons");
        }
    }
}
