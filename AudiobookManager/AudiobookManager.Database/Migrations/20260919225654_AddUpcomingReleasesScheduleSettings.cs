using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUpcomingReleasesScheduleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults match Database.Models.LibrarySettings' property initializers so that
            // existing rows backfill to the same values a freshly bootstrapped row gets: once a
            // day at 03:00 UTC, enabled - not an empty/disabled schedule that would silently turn
            // the check off for every installation upgrading into this migration.
            migrationBuilder.AddColumn<string>(
                name: "upcoming_releases_cron_schedule",
                table: "library_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "0 3 * * *");

            migrationBuilder.AddColumn<bool>(
                name: "upcoming_releases_enabled",
                table: "library_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "scheduled_task_runs",
                columns: table => new
                {
                    task_key = table.Column<string>(type: "TEXT", nullable: false),
                    last_run_started_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_run_duration_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    last_run_status = table.Column<string>(type: "TEXT", nullable: true),
                    last_run_error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_task_runs", x => x.task_key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_task_runs");

            migrationBuilder.DropColumn(
                name: "upcoming_releases_cron_schedule",
                table: "library_settings");

            migrationBuilder.DropColumn(
                name: "upcoming_releases_enabled",
                table: "library_settings");
        }
    }
}
