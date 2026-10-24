using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <summary>
    /// #1179: base stats and shared implicits change the item model under every copy, so every equipment item a character
    /// owns is removed (owner, 2026-10-24): the 79 equipment templates (74 bases and 5 uniques: 5-8, 12-26, 32-50, 58 and
    /// 64-103), in bags, banks and worn. The slots that held them and their modifier rows go with them (CharacterInventory,
    /// ItemInstanceModifiers and ItemInstanceModifierValues cascade from ItemInstances); consumables, scrolls and quest
    /// items stay. Trade records keep naming the wiped copies as history. No model change. Irreversible: Down restores
    /// nothing.
    /// </summary>
    public partial class WipeOwnedEquipmentForBaseStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM \"ItemInstances\" WHERE \"TemplateId\" IN (5, 6, 7, 8, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 58, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The wiped items are gone for good; there is nothing to put back.
        }
    }
}
