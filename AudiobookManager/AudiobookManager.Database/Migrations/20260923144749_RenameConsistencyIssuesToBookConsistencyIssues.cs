using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class RenameConsistencyIssuesToBookConsistencyIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "consistency_issues",
                newName: "book_consistency_issues");

            // RenameIndex is avoided here (rather than relying on it, as SQLite's migrations
            // generator only supports it in narrow adjacent-operation patterns and throws
            // NotSupportedException otherwise - see the pre-existing "Audiobooks added to db"
            // migration for a standalone RenameIndex that already hits this in this repo's own
            // down-migration tests).
            migrationBuilder.DropIndex(
                name: "ix_consistency_issues_audiobook_id",
                table: "book_consistency_issues");

            migrationBuilder.CreateIndex(
                name: "ix_book_consistency_issues_audiobook_id",
                table: "book_consistency_issues",
                column: "audiobook_id");

            // SQLite has no ALTER TABLE ... RENAME CONSTRAINT, so the inline foreign-key
            // constraint text baked into the table's original CREATE TABLE DDL keeps its old
            // name (fk_consistency_issues_...) after the rename above. Purely cosmetic - SQLite
            // does not look up foreign keys by name at runtime - so it is left as-is rather than
            // rebuilding the table just to relabel it.

            migrationBuilder.Sql(
                "UPDATE sqlite_sequence SET name = 'book_consistency_issues' WHERE name = 'consistency_issues';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_book_consistency_issues_audiobook_id",
                table: "book_consistency_issues");

            migrationBuilder.CreateIndex(
                name: "ix_consistency_issues_audiobook_id",
                table: "book_consistency_issues",
                column: "audiobook_id");

            migrationBuilder.RenameTable(
                name: "book_consistency_issues",
                newName: "consistency_issues");

            migrationBuilder.Sql(
                "UPDATE sqlite_sequence SET name = 'consistency_issues' WHERE name = 'book_consistency_issues';");
        }
    }
}
