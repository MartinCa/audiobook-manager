using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataChoiceRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metadata_choice_rules_json",
                table: "library_settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "metadata_choice_rules_json",
                table: "library_settings");
        }
    }
}
