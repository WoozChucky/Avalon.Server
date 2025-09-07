using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Salt = table.Column<byte[]>(type: "longblob", nullable: false),
                    Verifier = table.Column<byte[]>(type: "longblob", nullable: false),
                    SessionKey = table.Column<byte[]>(type: "longblob", nullable: false),
                    Email = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JoinDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastIp = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastAttemptIp = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailedLogins = table.Column<int>(type: "int", nullable: false),
                    Locked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Online = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MuteTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MuteReason = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MuteBy = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Locale = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Os = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalTime = table.Column<long>(type: "bigint", nullable: false),
                    AccessLevel = table.Column<ushort>(type: "smallint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Worlds",
                columns: table => new
                {
                    Id = table.Column<ushort>(type: "smallint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    AccessLevelRequired = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Host = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Port = table.Column<int>(type: "int", nullable: false),
                    MinVersion = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Worlds", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AccountId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Metadata = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Trusted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrustEnd = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastUsage = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MfaSetups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AccountId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Secret = table.Column<byte[]>(type: "longblob", nullable: false),
                    RecoveryCode1 = table.Column<byte[]>(type: "longblob", nullable: false),
                    RecoveryCode2 = table.Column<byte[]>(type: "longblob", nullable: false),
                    RecoveryCode3 = table.Column<byte[]>(type: "longblob", nullable: false),
                    Status = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AccountId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Index = table.Column<uint>(type: "int unsigned", nullable: false),
                    Hash = table.Column<byte[]>(type: "longblob", nullable: false),
                    Revoked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Usages = table.Column<uint>(type: "int unsigned", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AccountId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Hash = table.Column<byte[]>(type: "longblob", nullable: false),
                    Revoked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "AccessLevel", "Email", "FailedLogins", "JoinDate", "LastAttemptIp", "LastIp", "LastLogin", "Locale", "Locked", "MuteBy", "MuteReason", "MuteTime", "Online", "Os", "Salt", "SessionKey", "TotalTime", "Username", "Verifier" },
                values: new object[] { 1ul, (ushort)3, "admin@avalon.monster", 0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "", "127.0.0.1", new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "ptPT", false, "", "", null, false, "Linux", new byte[] { 36, 50, 97, 36, 49, 49, 36, 118, 75, 111, 115, 114, 98, 57, 102, 65, 87, 85, 46, 100, 115, 105, 54, 118, 81, 112, 107, 51, 117 }, new byte[0], 0L, "ADMIN", new byte[] { 36, 50, 97, 36, 49, 49, 36, 118, 75, 111, 115, 114, 98, 57, 102, 65, 87, 85, 46, 100, 115, 105, 54, 118, 81, 112, 107, 51, 117, 116, 87, 81, 107, 87, 68, 98, 90, 113, 71, 111, 102, 71, 104, 113, 78, 74, 56, 75, 57, 89, 75, 111, 47, 52, 118, 98, 77, 70, 47, 83 } });

            migrationBuilder.InsertData(
                table: "Worlds",
                columns: new[] { "Id", "AccessLevelRequired", "CreatedAt", "Host", "MinVersion", "Name", "Port", "Status", "Type", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { (ushort)1, (ushort)2, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "127.0.0.1", "0.0.1", "Development", 21001, (ushort)1, (ushort)0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "0.0.1" },
                    { (ushort)2, (ushort)0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "asthoria.avalon.monster", "0.0.1", "Asthoria", 21001, (ushort)0, (ushort)0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "0.0.1" },
                    { (ushort)3, (ushort)8, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "ptr.avalon.monster", "0.0.1", "Public Test Realm", 21001, (ushort)0, (ushort)0, new DateTime(2021, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "0.0.1" }
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
