using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Crashes.Migrations
{
    /// <inheritdoc />
    public partial class InitialCrashAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crash_discovery_cursor",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    continuation_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    backfill_continuation_token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    last_scan_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_backfill_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crash_discovery_cursor", x => x.id);
                    table.CheckConstraint("ck_crash_discovery_cursor_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "crash_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    signature_version = table.Column<int>(type: "integer", nullable: false),
                    signature_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    canonical_signature = table.Column<string>(type: "text", nullable: false),
                    signature = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    first_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    report_count = table.Column<long>(type: "bigint", nullable: false),
                    first_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "new"),
                    fixed_in_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    issue_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    notes = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_regression = table.Column<bool>(type: "boolean", nullable: false),
                    regression_count = table.Column<long>(type: "bigint", nullable: false),
                    last_regression_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    triage_revision = table.Column<long>(type: "bigint", nullable: false),
                    triaged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fixed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crash_groups", x => x.id);
                    table.CheckConstraint("ck_crash_groups_counts", "report_count >= 0 AND regression_count >= 0 AND triage_revision >= 0");
                    table.CheckConstraint("ck_crash_groups_extrema", "first_seen <= last_seen");
                    table.CheckConstraint("ck_crash_groups_hash", "signature_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_crash_groups_status", "status IN ('new','investigating','fixed','ignored')");
                });

            migrationBuilder.CreateTable(
                name: "crash_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    source_etag = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    source_size = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    git_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    meta = table.Column<string>(type: "jsonb", nullable: true),
                    client_crashed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason_detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    attempt_count = table.Column<long>(type: "bigint", nullable: false),
                    first_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_campaign_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consecutive_tool_failures = table.Column<long>(type: "bigint", nullable: false),
                    last_manual_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_owner = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completion_token = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    signature_version = table.Column<int>(type: "integer", nullable: true),
                    exception_code = table.Column<long>(type: "bigint", nullable: true),
                    exception_address = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    crashing_module = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    os_version = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    os_build = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    cpu = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    gpu_driver_module = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    gpu_driver_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    gpu_driver_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    stack = table.Column<string>(type: "jsonb", nullable: true),
                    stack_schema_version = table.Column<int>(type: "integer", nullable: true),
                    quality_flags = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    log_tail = table.Column<string>(type: "text", nullable: true),
                    processor_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    converter_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    cache_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crash_reports", x => x.id);
                    table.CheckConstraint("ck_crash_reports_attempts", "attempt_count >= 0 AND consecutive_tool_failures >= 0");
                    table.CheckConstraint("ck_crash_reports_driver_source", "gpu_driver_source IS NULL OR gpu_driver_source IN ('faulting_module','loaded_module','unknown')");
                    table.CheckConstraint("ck_crash_reports_exception_code", "exception_code IS NULL OR exception_code BETWEEN 0 AND 4294967295");
                    table.CheckConstraint("ck_crash_reports_expiry", "expires_at <= received_at + INTERVAL '90 days' AND expires_at > received_at");
                    table.CheckConstraint("ck_crash_reports_key_bytes", "octet_length(object_key) BETWEEN 1 AND 1024");
                    table.CheckConstraint("ck_crash_reports_lease", "(status = 'processing') = (lease_owner IS NOT NULL AND lease_until IS NOT NULL)");
                    table.CheckConstraint("ck_crash_reports_log_size", "log_tail IS NULL OR octet_length(log_tail) <= 65536");
                    table.CheckConstraint("ck_crash_reports_no_failed_input", "status NOT IN ('rejected','failed') OR (meta IS NULL AND stack IS NULL AND log_tail IS NULL AND version IS NULL AND git_hash IS NULL AND client_crashed_at IS NULL)");
                    table.CheckConstraint("ck_crash_reports_processed", "(status = 'processed') = (group_id IS NOT NULL AND stack IS NOT NULL AND processed_at IS NOT NULL AND signature_version IS NOT NULL)");
                    table.CheckConstraint("ck_crash_reports_quality", "quality_flags ~ '^(none|(missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable)(, (missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable))*)$'");
                    table.CheckConstraint("ck_crash_reports_reason", "reason_code IS NULL OR reason_code IN ('unsafe_archive','archive_too_large','metadata_too_large','log_too_large','invalid_metadata','unsupported_schema','invalid_dump','unsupported_architecture','runtime_identity_mismatch','object_changed','storage_unavailable','missing_runtime_symbols','tool_timeout','tool_failed','resource_limit','invalid_analysis','ungroupable_analysis','configuration_invalid','access_denied','object_missing')");
                    table.CheckConstraint("ck_crash_reports_source_size", "source_size >= 0");
                    table.CheckConstraint("ck_crash_reports_status", "status IN ('pending','processing','retry_wait','processed','rejected','failed')");
                    table.ForeignKey(
                        name: "FK_crash_reports_crash_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "crash_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "crash_discovery_cursor",
                columns: new[] { "id", "backfill_continuation_token", "continuation_token", "last_backfill_at", "last_scan_at" },
                values: new object[] { 1, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_crash_groups_signature_version_signature_hash",
                table: "crash_groups",
                columns: new[] { "signature_version", "signature_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crash_groups_status_last_seen",
                table: "crash_groups",
                columns: new[] { "status", "last_seen" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_crash_reports_expires_at",
                table: "crash_reports",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_crash_reports_group_id_received_at_id",
                table: "crash_reports",
                columns: new[] { "group_id", "received_at", "id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_crash_reports_lease_until",
                table: "crash_reports",
                column: "lease_until",
                filter: "status = 'processing'");

            migrationBuilder.CreateIndex(
                name: "IX_crash_reports_object_key",
                table: "crash_reports",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crash_reports_status_next_attempt_at",
                table: "crash_reports",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_crash_reports_version_received_at_group_id",
                table: "crash_reports",
                columns: new[] { "version", "received_at", "group_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crash_discovery_cursor");

            migrationBuilder.DropTable(
                name: "crash_reports");

            migrationBuilder.DropTable(
                name: "crash_groups");
        }
    }
}
