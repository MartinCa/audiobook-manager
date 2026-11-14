using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMainAuthorAndSourceSearchSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "main_author_only_in_path",
                table: "library_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "metadata_source_search_json",
                table: "library_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "main_author_id",
                table: "audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_main_author_id",
                table: "audiobooks",
                column: "main_author_id");

            migrationBuilder.AddForeignKey(
                name: "fk_audiobooks_persons_main_author_id",
                table: "audiobooks",
                column: "main_author_id",
                principalTable: "persons",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audiobooks_persons_main_author_id",
                table: "audiobooks");

            migrationBuilder.DropIndex(
                name: "ix_audiobooks_main_author_id",
                table: "audiobooks");

            migrationBuilder.DropColumn(
                name: "main_author_only_in_path",
                table: "library_settings");

            migrationBuilder.DropColumn(
                name: "metadata_source_search_json",
                table: "library_settings");

            migrationBuilder.DropColumn(
                name: "main_author_id",
                table: "audiobooks");
        }
    }
}
