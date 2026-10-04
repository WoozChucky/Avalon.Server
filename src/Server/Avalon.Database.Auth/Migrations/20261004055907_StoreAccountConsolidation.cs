using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class StoreAccountConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // Existing account credentials, links and session fences must survive this nullable column.
            migrationBuilder.AddColumn<Guid>(
                name: "GameplayConsolidationId",
                table: "Accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountConsolidations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<long>(type: "bigint", nullable: false),
                    TargetAccountId = table.Column<long>(type: "bigint", nullable: false),
                    SteamSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetCredentialsVersion = table.Column<int>(type: "integer", nullable: false),
                    TargetSessionEpoch = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedMfaId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountConsolidations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountConsolidationWorlds",
                columns: table => new
                {
                    ConsolidationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorldId = table.Column<int>(type: "integer", nullable: false),
                    TransferredCharacters = table.Column<int>(type: "integer", nullable: false),
                    TransferredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GuardReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountConsolidationWorlds", x => new { x.ConsolidationId, x.WorldId });
                    table.ForeignKey(
                        name: "FK_AccountConsolidationWorlds_AccountConsolidations_Consolidat~",
                        column: x => x.ConsolidationId,
                        principalTable: "AccountConsolidations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });


            migrationBuilder.CreateIndex(
                name: "IX_AccountConsolidations_SourceAccountId",
                table: "AccountConsolidations",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountConsolidations_TargetAccountId",
                table: "AccountConsolidations",
                column: "TargetAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountConsolidationWorlds");

            migrationBuilder.DropTable(
                name: "AccountConsolidations");

            migrationBuilder.DropColumn(
                name: "GameplayConsolidationId",
                table: "Accounts");
        }
    }
}
