using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuedOrganizeTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "queued_organize_task",
                columns: table => new
                {
                    original_file_location = table.Column<string>(type: "TEXT", nullable: false),
                    json_audiobook = table.Column<string>(type: "TEXT", nullable: false),
                    queued_time = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_queued_organize_task", x => x.original_file_location);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queued_organize_task");
        }
    }
}
