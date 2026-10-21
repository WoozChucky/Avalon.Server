using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class TrackRepeatedQuestCompletions : Migration
    {
        /// <summary>
        /// Repeatable quests (#710): a completed quest keeps one row and counts its turn-ins. Every stored row was turned
        /// in once, so its count starts at 1 and its last turn-in is its first. The defaults only fill the stored rows:
        /// they are dropped after the back-fill, as the model declares none and the world server writes both columns.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompletionCount",
                table: "CharacterCompletedQuests",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCompletedAt",
                table: "CharacterCompletedQuests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("UPDATE \"CharacterCompletedQuests\" SET \"LastCompletedAt\" = \"CompletedAt\";");

            migrationBuilder.AlterColumn<long>(
                name: "CompletionCount",
                table: "CharacterCompletedQuests",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 1L);

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastCompletedAt",
                table: "CharacterCompletedQuests",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionCount",
                table: "CharacterCompletedQuests");

            migrationBuilder.DropColumn(
                name: "LastCompletedAt",
                table: "CharacterCompletedQuests");
        }
    }
}
