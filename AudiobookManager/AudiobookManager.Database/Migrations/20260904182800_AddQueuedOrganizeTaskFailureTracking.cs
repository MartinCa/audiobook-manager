using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuedOrganizeTaskFailureTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failure_count",
                table: "queued_organize_task",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_failure_at",
                table: "queued_organize_task",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_failure_reason",
                table: "queued_organize_task",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure_count",
                table: "queued_organize_task");

            migrationBuilder.DropColumn(
                name: "last_failure_at",
                table: "queued_organize_task");

            migrationBuilder.DropColumn(
                name: "last_failure_reason",
                table: "queued_organize_task");
        }
    }
}
