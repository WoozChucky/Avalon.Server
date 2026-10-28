using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Crashes.Migrations
{
    /// <inheritdoc />
    public partial class CrashGpuColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_no_failed_input",
                table: "crash_reports");

            migrationBuilder.AddColumn<string>(
                name: "gpu_name",
                table: "crash_reports",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vulkan_driver_version",
                table: "crash_reports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_no_failed_input",
                table: "crash_reports",
                sql: "status NOT IN ('rejected','failed') OR (meta IS NULL AND stack IS NULL AND log_tail IS NULL AND version IS NULL AND git_hash IS NULL AND client_crashed_at IS NULL AND gpu_name IS NULL AND vulkan_driver_version IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_no_failed_input",
                table: "crash_reports");

            migrationBuilder.DropColumn(
                name: "gpu_name",
                table: "crash_reports");

            migrationBuilder.DropColumn(
                name: "vulkan_driver_version",
                table: "crash_reports");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_no_failed_input",
                table: "crash_reports",
                sql: "status NOT IN ('rejected','failed') OR (meta IS NULL AND stack IS NULL AND log_tail IS NULL AND version IS NULL AND git_hash IS NULL AND client_crashed_at IS NULL)");
        }
    }
}
