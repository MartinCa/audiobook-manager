using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingOnlineMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_online_match",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    audiobook_id = table.Column<long>(type: "INTEGER", nullable: false),
                    searched_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    source_names_json = table.Column<string>(type: "TEXT", nullable: false),
                    results_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_online_match", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_online_match_audiobooks_audiobook_id",
                        column: x => x.audiobook_id,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_online_match_audiobook_id",
                table: "pending_online_match",
                column: "audiobook_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_online_match");
        }
    }
}
