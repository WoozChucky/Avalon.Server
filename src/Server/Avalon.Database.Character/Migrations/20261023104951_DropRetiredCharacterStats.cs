using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class DropRetiredCharacterStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbilityDamage",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "AttackDamage",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "DodgePct",
                table: "CharacterStats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AbilityDamage",
                table: "CharacterStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AttackDamage",
                table: "CharacterStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "DodgePct",
                table: "CharacterStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }
    }
}
