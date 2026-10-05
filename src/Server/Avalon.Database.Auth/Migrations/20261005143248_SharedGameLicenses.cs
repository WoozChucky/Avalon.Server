using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SharedGameLicenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProviderAppId",
                table: "LicenseObservations",
                newName: "ProviderProductId");

            migrationBuilder.AddColumn<long>(
                name: "AuthorityRevision",
                table: "LicenseObservations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LicenseId",
                table: "LicenseObservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameLicenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Product = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderProductId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LicenseReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthorityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AuthorityRevision = table.Column<long>(type: "bigint", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameLicenses", x => x.Id);
                    table.CheckConstraint("CK_GameLicenses_Interval", "(\"ExpiresAt\" IS NULL OR \"ExpiresAt\" > \"GrantedAt\") AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"GrantedAt\")");
                    table.CheckConstraint("CK_GameLicenses_Reference", "length(trim(\"LicenseReference\")) > 0 AND \"LicenseReference\" = trim(\"LicenseReference\")");
                    table.CheckConstraint("CK_GameLicenses_Revision", "\"AuthorityRevision\" > 0 AND \"AccountId\" > 0");
                    table.ForeignKey(
                        name: "FK_GameLicenses_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseObservations_LicenseId",
                table: "LicenseObservations",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_GameLicenses_AccountId_Product_Provider_Environment",
                table: "GameLicenses",
                columns: new[] { "AccountId", "Product", "Provider", "Environment" });

            migrationBuilder.CreateIndex(
                name: "IX_GameLicenses_Provider_Environment_LicenseReference",
                table: "GameLicenses",
                columns: new[] { "Provider", "Environment", "LicenseReference" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LicenseObservations_GameLicenses_LicenseId",
                table: "LicenseObservations",
                column: "LicenseId",
                principalTable: "GameLicenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LicenseObservations_GameLicenses_LicenseId",
                table: "LicenseObservations");

            migrationBuilder.DropTable(
                name: "GameLicenses");

            migrationBuilder.DropIndex(
                name: "IX_LicenseObservations_LicenseId",
                table: "LicenseObservations");

            migrationBuilder.DropColumn(
                name: "AuthorityRevision",
                table: "LicenseObservations");

            migrationBuilder.DropColumn(
                name: "LicenseId",
                table: "LicenseObservations");

            migrationBuilder.RenameColumn(
                name: "ProviderProductId",
                table: "LicenseObservations",
                newName: "ProviderAppId");
        }
    }
}
