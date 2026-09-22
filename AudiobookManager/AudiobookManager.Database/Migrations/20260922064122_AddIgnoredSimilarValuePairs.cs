using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddIgnoredSimilarValuePairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ignored_similar_value_pairs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    value_a = table.Column<string>(type: "TEXT", nullable: false),
                    value_b = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ignored_similar_value_pairs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ignored_similar_value_pairs_kind_value_a_value_b",
                table: "ignored_similar_value_pairs",
                columns: new[] { "kind", "value_a", "value_b" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ignored_similar_value_pairs");
        }
    }
}
