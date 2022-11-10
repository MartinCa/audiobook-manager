using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    public partial class InitialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "series_mapping",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    regex = table.Column<string>(type: "TEXT", nullable: false),
                    mapped_series = table.Column<string>(type: "TEXT", nullable: false),
                    warn_about_part = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_mapping", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_series_mapping_regex",
                table: "series_mapping",
                column: "regex",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_mapping");
        }
    }
}
