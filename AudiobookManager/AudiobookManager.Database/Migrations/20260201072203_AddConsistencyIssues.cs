using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddConsistencyIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consistency_issues",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    audiobook_id = table.Column<long>(type: "INTEGER", nullable: false),
                    issue_type = table.Column<int>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    expected_value = table.Column<string>(type: "TEXT", nullable: true),
                    actual_value = table.Column<string>(type: "TEXT", nullable: true),
                    detected_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consistency_issues", x => x.id);
                    table.ForeignKey(
                        name: "fk_consistency_issues_audiobooks_audiobook_id",
                        column: x => x.audiobook_id,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consistency_issues_audiobook_id",
                table: "consistency_issues",
                column: "audiobook_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consistency_issues");
        }
    }
}
