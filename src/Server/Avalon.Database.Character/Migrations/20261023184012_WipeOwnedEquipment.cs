using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <summary>
    /// #1179: equipment became bases and uniques, so every equipment item a character owns is removed: the 52 retired
    /// equipment templates (4-8, 12-55, 58, 64 and 65), in bags, banks and worn. The slots that held them go with them
    /// (CharacterInventory cascades from ItemInstances); consumables and quest items stay. No model change.
    /// Irreversible: Down restores nothing.
    /// </summary>
    public partial class WipeOwnedEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM \"ItemInstances\" WHERE \"TemplateId\" IN (4, 5, 6, 7, 8, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 58, 64, 65);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The wiped items are gone for good; there is nothing to put back.
        }
    }
}
