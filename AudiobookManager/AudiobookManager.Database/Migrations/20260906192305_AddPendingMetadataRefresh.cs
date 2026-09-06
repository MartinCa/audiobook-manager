using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingMetadataRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "metadata_refresh_delay_ms",
                table: "library_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.CreateTable(
                name: "pending_metadata_refresh",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    audiobook_id = table.Column<long>(type: "INTEGER", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    source_name = table.Column<string>(type: "TEXT", nullable: false),
                    source_url = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_metadata_refresh", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_metadata_refresh_audiobooks_audiobook_id",
                        column: x => x.audiobook_id,
                        principalTable: "audiobooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_metadata_refresh_audiobook_id",
                table: "pending_metadata_refresh",
                column: "audiobook_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_metadata_refresh");

            migrationBuilder.DropColumn(
                name: "metadata_refresh_delay_ms",
                table: "library_settings");
        }
    }
}
