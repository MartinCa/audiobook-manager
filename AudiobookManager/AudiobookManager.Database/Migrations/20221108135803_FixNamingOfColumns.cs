using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    public partial class FixNamingOfColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Regex",
                table: "series_mapping",
                newName: "regex");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "series_mapping",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WarnAboutPart",
                table: "series_mapping",
                newName: "warn_about_part");

            migrationBuilder.RenameColumn(
                name: "MappedSeries",
                table: "series_mapping",
                newName: "mapped_series");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "regex",
                table: "series_mapping",
                newName: "Regex");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "series_mapping",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "warn_about_part",
                table: "series_mapping",
                newName: "WarnAboutPart");

            migrationBuilder.RenameColumn(
                name: "mapped_series",
                table: "series_mapping",
                newName: "MappedSeries");
        }
    }
}
