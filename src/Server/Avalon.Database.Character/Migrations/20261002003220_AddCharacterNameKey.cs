using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterNameKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "Characters",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Every existing character's key is its upper-cased name (#757), filled before the unique index is built:
            // on the empty default every row would clash. COLLATE "C" folds ASCII letters only, whatever the
            // database's locale, as the check constraint and CharacterName.Key do. Existing names are left as they
            // are (owner decision: none breaks the new rule or clashes with another by case).
            migrationBuilder.Sql("UPDATE \"Characters\" SET \"NameKey\" = upper(\"Name\" COLLATE \"C\");");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_NameKey",
                table: "Characters",
                column: "NameKey",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Characters_NameKey",
                table: "Characters",
                sql: "\"NameKey\" = upper(\"Name\" COLLATE \"C\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Characters_NameKey",
                table: "Characters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Characters_NameKey",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "Characters");
        }
    }
}
