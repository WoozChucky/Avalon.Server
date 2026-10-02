using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterCompletedQuests",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCompletedQuests", x => new { x.CharacterId, x.QuestId });
                    table.ForeignKey(
                        name: "FK_CharacterCompletedQuests_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterQuests",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<byte>(type: "smallint", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuests", x => new { x.CharacterId, x.QuestId });
                    table.ForeignKey(
                        name: "FK_CharacterQuests_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterQuestObjectives",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    ObjectiveId = table.Column<long>(type: "bigint", nullable: false),
                    Progress = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuestObjectives", x => new { x.CharacterId, x.QuestId, x.ObjectiveId });
                    table.ForeignKey(
                        name: "FK_CharacterQuestObjectives_CharacterQuests_CharacterId_QuestId",
                        columns: x => new { x.CharacterId, x.QuestId },
                        principalTable: "CharacterQuests",
                        principalColumns: new[] { "CharacterId", "QuestId" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterCompletedQuests");

            migrationBuilder.DropTable(
                name: "CharacterQuestObjectives");

            migrationBuilder.DropTable(
                name: "CharacterQuests");
        }
    }
}
