using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddGlyphTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BaseKind", "BaseTemplateId", "BuyPrice", "Class", "DamageType", "DisplayId", "Flags", "MaxStackSize", "Name", "Rarity", "RequiredAgility", "RequiredIntellect", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass", "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { 152m, new byte[0], null, null, 0L, 4, null, 152L, 0, 1L, "Glyph", (byte)0, null, null, null, null, false, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 401, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 152m);
        }
    }
}
