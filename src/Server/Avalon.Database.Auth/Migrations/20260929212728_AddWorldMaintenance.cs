using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MaintenanceDeadlineUtc",
                table: "Worlds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaintenanceEnabled",
                table: "Worlds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "MaintenanceRevision",
                table: "Worlds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MaintenanceDeadlineUtc", "MaintenanceEnabled", "MaintenanceRevision" },
                values: new object[] { null, false, 0L });

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MaintenanceDeadlineUtc", "MaintenanceEnabled", "MaintenanceRevision" },
                values: new object[] { null, false, 0L });

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MaintenanceDeadlineUtc", "MaintenanceEnabled", "MaintenanceRevision" },
                values: new object[] { null, false, 0L });

            // Existing operator-set maintenance must survive the schema change. The seeded
            // worlds above are Online/Offline; a deployment may have changed any row since.
            migrationBuilder.Sql("""
                UPDATE "Worlds"
                SET "MaintenanceEnabled" = TRUE,
                    "MaintenanceRevision" = 1,
                    "MaintenanceDeadlineUtc" = CURRENT_TIMESTAMP + INTERVAL '5 minutes'
                WHERE "Status" = 2
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaintenanceDeadlineUtc",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "MaintenanceEnabled",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "MaintenanceRevision",
                table: "Worlds");
        }
    }
}
