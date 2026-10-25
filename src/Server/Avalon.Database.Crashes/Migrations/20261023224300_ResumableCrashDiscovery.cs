using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Crashes.Migrations
{
    /// <inheritdoc />
    public partial class ResumableCrashDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_reason",
                table: "crash_reports");

            migrationBuilder.CreateTable(
                name: "crash_discovery_versions",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_visited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recent_date = table.Column<DateOnly>(type: "date", nullable: true),
                    today_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    yesterday_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    date_prefix_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    date_inventory_ready = table.Column<bool>(type: "boolean", nullable: false),
                    historical_date = table.Column<DateOnly>(type: "date", nullable: true),
                    historical_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    phase = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crash_discovery_versions", x => x.version);
                    table.CheckConstraint("ck_crash_discovery_versions_phase", "phase BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_crash_discovery_versions_version", "version ~ '^[A-Za-z0-9.+-]{1,64}$' AND version NOT IN ('.','..')");
                });

            migrationBuilder.CreateTable(
                name: "crash_discovery_dates",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crash_discovery_dates", x => new { x.version, x.date });
                    table.ForeignKey(
                        name: "FK_crash_discovery_dates_crash_discovery_versions_version",
                        column: x => x.version,
                        principalTable: "crash_discovery_versions",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_reason",
                table: "crash_reports",
                sql: "reason_code IS NULL OR reason_code IN ('unsafe_archive','archive_too_large','metadata_too_large','log_too_large','invalid_metadata','unsupported_schema','invalid_dump','unsupported_architecture','runtime_identity_mismatch','object_changed','storage_unavailable','missing_runtime_symbols','tool_timeout','tool_failed','resource_limit','invalid_analysis','ungroupable_analysis','configuration_invalid','access_denied','object_missing','invalid_object_key')");

            migrationBuilder.CreateIndex(
                name: "IX_crash_discovery_versions_last_seen_at",
                table: "crash_discovery_versions",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "IX_crash_discovery_versions_last_visited_at_version",
                table: "crash_discovery_versions",
                columns: new[] { "last_visited_at", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crash_discovery_dates");

            migrationBuilder.DropTable(
                name: "crash_discovery_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_reason",
                table: "crash_reports");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_reason",
                table: "crash_reports",
                sql: "reason_code IS NULL OR reason_code IN ('unsafe_archive','archive_too_large','metadata_too_large','log_too_large','invalid_metadata','unsupported_schema','invalid_dump','unsupported_architecture','runtime_identity_mismatch','object_changed','storage_unavailable','missing_runtime_symbols','tool_timeout','tool_failed','resource_limit','invalid_analysis','ungroupable_analysis','configuration_invalid','access_denied','object_missing')");
        }
    }
}
