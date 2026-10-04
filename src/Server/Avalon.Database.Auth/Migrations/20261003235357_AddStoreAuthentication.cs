using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SessionEpoch",
                table: "Accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentities_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    GameSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false),
                    ServerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorldId = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CredentialsVersion = table.Column<int>(type: "integer", nullable: false),
                    SessionEpoch = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LicenseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousGameSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousServerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PreviousWorldId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.AccountId);
                    table.CheckConstraint("CK_GameSessions_FencingToken", "\"FencingToken\" > 0");
                    table.ForeignKey(
                        name: "FK_GameSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicenseObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Product = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderAppId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderOwnerSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OwnsProduct = table.Column<bool>(type: "boolean", nullable: false),
                    Permanent = table.Column<bool>(type: "boolean", nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorizedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProviderExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseObservations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "SessionEpoch",
                value: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_SessionEpoch",
                table: "Accounts",
                sql: "\"SessionEpoch\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_AccountId_Provider",
                table: "ExternalIdentities",
                columns: new[] { "AccountId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_ProviderSubject",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_GameSessionId",
                table: "GameSessions",
                column: "GameSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseObservations_AccountId_Provider_ProviderSubject_Envi~",
                table: "LicenseObservations",
                columns: new[] { "AccountId", "Provider", "ProviderSubject", "Environment", "Product", "ObservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIdentities");

            migrationBuilder.DropTable(
                name: "GameSessions");

            migrationBuilder.DropTable(
                name: "LicenseObservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_SessionEpoch",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SessionEpoch",
                table: "Accounts");
        }
    }
}
