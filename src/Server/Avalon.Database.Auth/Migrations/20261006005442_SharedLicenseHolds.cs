using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SharedLicenseHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAt",
                table: "GameLicenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LicenseHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CauseKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CauseReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseHolds", x => x.Id);
                    table.CheckConstraint("CK_LicenseHolds_Interval", "\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"StartedAt\"");
                    table.ForeignKey(
                        name: "FK_LicenseHolds_GameLicenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "GameLicenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseHolds_LicenseId_CauseKind_CauseReference",
                table: "LicenseHolds",
                columns: new[] { "LicenseId", "CauseKind", "CauseReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseHolds");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "GameLicenses");
        }
    }
}
