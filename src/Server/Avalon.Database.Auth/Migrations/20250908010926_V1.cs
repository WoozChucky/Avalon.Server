using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class V1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Salt = table.Column<byte[]>(type: "bytea", nullable: false),
                    Verifier = table.Column<byte[]>(type: "bytea", nullable: false),
                    SessionKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    JoinDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastIp = table.Column<string>(type: "text", nullable: false),
                    LastAttemptIp = table.Column<string>(type: "text", nullable: false),
                    FailedLogins = table.Column<int>(type: "integer", nullable: false),
                    Locked = table.Column<bool>(type: "boolean", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Online = table.Column<bool>(type: "boolean", nullable: false),
                    MuteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MuteReason = table.Column<string>(type: "text", nullable: false),
                    MuteBy = table.Column<string>(type: "text", nullable: false),
                    Locale = table.Column<string>(type: "text", nullable: false),
                    Os = table.Column<string>(type: "text", nullable: false),
                    TotalTime = table.Column<long>(type: "bigint", nullable: false),
                    AccessLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Worlds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    AccessLevelRequired = table.Column<int>(type: "integer", nullable: false),
                    Host = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    MinVersion = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Worlds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: false),
                    Trusted = table.Column<bool>(type: "boolean", nullable: false),
                    TrustEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsage = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfaSetups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    RecoveryCode1 = table.Column<byte[]>(type: "bytea", nullable: false),
                    RecoveryCode2 = table.Column<byte[]>(type: "bytea", nullable: false),
                    RecoveryCode3 = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaSetups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaSetups_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Index = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    Usages = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tokens_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "AccessLevel", "Email", "FailedLogins", "JoinDate", "LastAttemptIp", "LastIp", "LastLogin", "Locale", "Locked", "MuteBy", "MuteReason", "MuteTime", "Online", "Os", "Salt", "SessionKey", "TotalTime", "Username", "Verifier" },
                values: new object[] { 1L, 3, "admin@avalon.monster", 0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "", "127.0.0.1", new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ptPT", false, "", "", null, false, "Linux", new byte[] { 36, 50, 97, 36, 49, 49, 36, 87, 99, 81, 111, 50, 73, 79, 51, 110, 69, 119, 75, 77, 78, 85, 98, 116, 110, 71, 88, 90, 46 }, new byte[0], 0L, "ADMIN", new byte[] { 36, 50, 97, 36, 49, 49, 36, 87, 99, 81, 111, 50, 73, 79, 51, 110, 69, 119, 75, 77, 78, 85, 98, 116, 110, 71, 88, 90, 46, 54, 72, 106, 115, 116, 79, 46, 107, 82, 120, 110, 46, 80, 115, 80, 83, 85, 98, 55, 47, 70, 103, 116, 50, 69, 97, 119, 107, 53, 105, 54 } });

            migrationBuilder.InsertData(
                table: "Worlds",
                columns: new[] { "Id", "AccessLevelRequired", "CreatedAt", "Host", "MinVersion", "Name", "Port", "Status", "Type", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, 2, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "127.0.0.1", "0.0.1", "Development", 21001, 1, 0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "0.0.1" },
                    { 2, 0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "asthoria.avalon.monster", "0.0.1", "Asthoria", 21001, 0, 0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "0.0.1" },
                    { 3, 8, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ptr.avalon.monster", "0.0.1", "Public Test Realm", 21001, 0, 0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "0.0.1" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_AccountId",
                table: "Devices",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaSetups_AccountId",
                table: "MfaSetups",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AccountId",
                table: "RefreshTokens",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_AccountId",
                table: "Tokens",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "MfaSetups");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Tokens");

            migrationBuilder.DropTable(
                name: "Worlds");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
