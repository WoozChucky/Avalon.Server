using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class RenameSpellsToAbilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing constraints/indexes that embed the old table name so we can recreate
            // them with the new naming. PostgreSQL keeps PK/FK/index identifiers stable across a
            // simple ALTER TABLE ... RENAME TO, so we rename them explicitly to the EF convention.
            migrationBuilder.DropForeignKey(
                name: "FK_CharacterSpells_Characters_CharacterId",
                table: "CharacterSpells");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CharacterSpells",
                table: "CharacterSpells");

            migrationBuilder.RenameIndex(
                name: "IX_CharacterSpells_CharacterId",
                table: "CharacterSpells",
                newName: "IX_CharacterAbilities_CharacterId");

            migrationBuilder.RenameColumn(
                name: "SpellId",
                table: "CharacterSpells",
                newName: "AbilityId");

            migrationBuilder.RenameTable(
                name: "CharacterSpells",
                newName: "CharacterAbilities");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CharacterAbilities",
                table: "CharacterAbilities",
                columns: new[] { "CharacterId", "AbilityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CharacterAbilities_Characters_CharacterId",
                table: "CharacterAbilities",
                column: "CharacterId",
                principalTable: "Characters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CharacterAbilities_Characters_CharacterId",
                table: "CharacterAbilities");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CharacterAbilities",
                table: "CharacterAbilities");

            migrationBuilder.RenameIndex(
                name: "IX_CharacterAbilities_CharacterId",
                table: "CharacterAbilities",
                newName: "IX_CharacterSpells_CharacterId");

            migrationBuilder.RenameColumn(
                name: "AbilityId",
                table: "CharacterAbilities",
                newName: "SpellId");

            migrationBuilder.RenameTable(
                name: "CharacterAbilities",
                newName: "CharacterSpells");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CharacterSpells",
                table: "CharacterSpells",
                columns: new[] { "CharacterId", "SpellId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CharacterSpells_Characters_CharacterId",
                table: "CharacterSpells",
                column: "CharacterId",
                principalTable: "Characters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
