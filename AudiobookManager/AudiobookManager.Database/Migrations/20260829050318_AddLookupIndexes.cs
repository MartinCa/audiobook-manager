using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_discovered_audiobooks_file_info_full_path",
                table: "discovered_audiobooks",
                column: "file_info_full_path");

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_file_info_file_name",
                table: "audiobooks",
                column: "file_info_file_name");

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_file_info_full_path",
                table: "audiobooks",
                column: "file_info_full_path");

            migrationBuilder.CreateIndex(
                name: "ix_audiobooks_series",
                table: "audiobooks",
                column: "series");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_discovered_audiobooks_file_info_full_path",
                table: "discovered_audiobooks");

            migrationBuilder.DropIndex(
                name: "ix_audiobooks_file_info_file_name",
                table: "audiobooks");

            migrationBuilder.DropIndex(
                name: "ix_audiobooks_file_info_full_path",
                table: "audiobooks");

            migrationBuilder.DropIndex(
                name: "ix_audiobooks_series",
                table: "audiobooks");
        }
    }
}
