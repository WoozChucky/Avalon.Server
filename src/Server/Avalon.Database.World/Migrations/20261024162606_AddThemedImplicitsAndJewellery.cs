using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #1179: every base carries a themed implicit, tier-matched (26 implicit groups, rows 287-364, one link per base);
    /// the rings and amulets come in lines of three tiers (templates 104-149) and the trinket uniques get their two
    /// unique-only bases (150, 151); every unique names its base (ItemTemplates.BaseTemplateId), and its own lines are
    /// re-authored to the starting zone's budget. Hand-ordered: the uniques are pointed at their bases only after the
    /// bases are inserted. Down drops the column and the new rows and restores the old unique lines.
    /// </summary>
    public partial class AddThemedImplicitsAndJewellery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 222 });

            migrationBuilder.AddColumn<decimal>(
                name: "BaseTemplateId",
                table: "ItemTemplates",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ItemModifierGroups",
                columns: new[] { "Id", "Code", "Element", "Kind", "Name" },
                values: new object[,]
                {
                    { 111, null, null, (byte)4, "Fire Resistance" },
                    { 112, null, null, (byte)4, "Cold Resistance" },
                    { 113, null, null, (byte)4, "Lightning Resistance" },
                    { 114, null, null, (byte)4, "Blight Resistance" },
                    { 115, null, null, (byte)4, "Increased Armor" },
                    { 116, null, null, (byte)4, "Increased Health" },
                    { 117, null, null, (byte)4, "Physical Damage to Attacks" },
                    { 118, null, null, (byte)4, "Strength" },
                    { 119, null, null, (byte)4, "Stamina" },
                    { 120, null, null, (byte)4, "Crit Multiplier" },
                    { 121, null, null, (byte)4, "Agility" },
                    { 122, null, null, (byte)4, "Increased Evasion" },
                    { 123, null, null, (byte)4, "Increased Projectile Damage" },
                    { 124, null, null, (byte)4, "Movement Speed" },
                    { 125, null, null, (byte)4, "Cooldown Recovery" },
                    { 126, null, null, (byte)4, "Intellect" },
                    { 127, null, null, (byte)4, "Increased Spell Damage" },
                    { 128, null, null, (byte)4, "Cast Speed" },
                    { 129, null, null, (byte)4, "Power" },
                    { 130, null, null, (byte)4, "Health" },
                    { 131, null, null, (byte)4, "Increased Physical Damage" },
                    { 132, null, null, (byte)4, "Lightning Damage to Attacks" },
                    { 133, null, null, (byte)4, "Fire Damage to Spells" },
                    { 134, null, null, (byte)4, "Cold Damage to Spells" },
                    { 135, null, null, (byte)4, "Increased Damage Over Time" },
                    { 136, null, null, (byte)4, "Increased Healing" }
                });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 219 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 219 },
                column: "Max",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 219 },
                column: "Max",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 220 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 220 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 220 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 7, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 221 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 221 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 221 },
                columns: new[] { "Max", "Stat" },
                values: new object[] { 4, (short)32 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 222 },
                columns: new[] { "Max", "Min", "Stat" },
                values: new object[] { 5, 4, (short)53 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 222 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 223 },
                columns: new[] { "Max", "Min", "Stat" },
                values: new object[] { 5, 4, (short)53 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 223 },
                column: "Stat",
                value: (short)25);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 223 },
                column: "Stat",
                value: (short)29);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 68m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 69m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 70m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 71m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 72m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 73m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 74m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 75m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 76m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 77m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 78m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 79m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 80m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 81m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 82m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 83m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 84m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 85m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 86m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 87m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 88m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 89m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 90m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 91m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 92m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 93m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 94m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 95m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 96m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 97m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 98m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 99m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 100m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 101m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 102m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 103m,
                column: "BaseTemplateId",
                value: null);

            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "AttackTimeMs", "BaseKind", "BaseTemplateId", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "MaxStackSize", "Name", "Rarity", "RequiredAgility", "RequiredIntellect", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass", "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[,]
                {
                    { 104m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 104L, 0, 1L, "Oakbark Band", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 105m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 105L, 0, 1L, "Elmroot Band", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 106m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 106L, 0, 1L, "Thornwire Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 107m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 107L, 0, 1L, "Briarsteel Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 108m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 108L, 0, 1L, "Bramblefang Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 109m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 109L, 0, 1L, "Emberknot Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 110m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 110L, 0, 1L, "Cinderknot Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 111m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 111L, 0, 1L, "Ashcoil Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 112m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 112L, 0, 1L, "Hearthstone Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 113m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 113L, 0, 1L, "Kilnstone Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 114m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 114L, 0, 1L, "Forgeheart Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 115m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 115L, 0, 1L, "Rimeglass Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 116m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 116L, 0, 1L, "Frostglass Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 117m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 117L, 0, 1L, "Winterglass Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 118m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 118L, 0, 1L, "Lodestone Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 119m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 119L, 0, 1L, "Stormlode Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 120m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 120L, 0, 1L, "Thunderlode Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 121m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 121L, 0, 1L, "Willowbark Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 122m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 122L, 0, 1L, "Fenwillow Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 123m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 123L, 0, 1L, "Elderwillow Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 124m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 124L, 0, 1L, "Sparkthorn Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 125m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 125L, 0, 1L, "Stormthorn Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 126m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 126L, 0, 1L, "Thunderthorn Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 127m, new byte[0], null, (byte)26, null, 200L, 2, null, null, null, null, null, null, 127L, 0, 1L, "Frostbud Ring", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 128m, new byte[0], null, (byte)26, null, 400L, 2, null, null, null, null, null, null, 128L, 0, 1L, "Rimebud Ring", (byte)0, null, null, 5, null, false, 100L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 129m, new byte[0], null, (byte)26, null, 800L, 2, null, null, null, null, null, null, 129L, 0, 1L, "Winterbud Ring", (byte)0, null, null, 10, null, false, 200L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 130m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 130L, 0, 1L, "Tusk Charm", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 131m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 131L, 0, 1L, "Horn Charm", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 132m, new byte[0], null, (byte)25, null, 200L, 2, null, null, null, null, null, null, 132L, 0, 1L, "Antler Charm", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 133m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 133L, 0, 1L, "Hartsong Charm", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 134m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 134L, 0, 1L, "Stagcrown Charm", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 135m, new byte[0], null, (byte)25, null, 200L, 2, null, null, null, null, null, null, 135L, 0, 1L, "Hollowfeather Charm", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 136m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 136L, 0, 1L, "Owlquill Charm", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 137m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 137L, 0, 1L, "Nightplume Charm", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 138m, new byte[0], null, (byte)25, null, 200L, 2, null, null, null, null, null, null, 138L, 0, 1L, "Gloam Pendant", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 139m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 139L, 0, 1L, "Mirk Pendant", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 140m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 140L, 0, 1L, "Nightmire Pendant", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 141m, new byte[0], null, (byte)25, null, 200L, 2, null, null, null, null, null, null, 141L, 0, 1L, "Mossheart Pendant", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 142m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 142L, 0, 1L, "Fernheart Pendant", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 143m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 143L, 0, 1L, "Elderheart Pendant", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 144m, new byte[0], null, (byte)25, null, 200L, 2, null, null, null, null, null, null, 144L, 0, 1L, "Foxtooth Pendant", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 145m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 145L, 0, 1L, "Wolfstooth Pendant", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 146m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 146L, 0, 1L, "Bearclaw Pendant", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 147m, new byte[0], null, (byte)25, null, 200L, 2, null, null, null, null, null, null, 147L, 0, 1L, "Dewdrop Pendant", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 148m, new byte[0], null, (byte)25, null, 400L, 2, null, null, null, null, null, null, 148L, 0, 1L, "Rainbead Pendant", (byte)0, null, null, 5, null, false, 100L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 149m, new byte[0], null, (byte)25, null, 800L, 2, null, null, null, null, null, null, 149L, 0, 1L, "Wellspring Pendant", (byte)0, null, null, 10, null, false, 200L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 150m, new byte[0], null, (byte)27, null, 200L, 2, null, null, null, null, null, null, 150L, 0, 1L, "Withered Seed", (byte)0, null, null, 1, null, false, 50L, 11, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 210, null, null, null, null, null },
                    { 151m, new byte[0], null, (byte)27, null, 200L, 2, null, null, null, null, null, null, 151L, 0, 1L, "Carved Idol", (byte)0, null, null, 1, null, false, 50L, 11, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 210, null, null, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "ItemModifiers",
                columns: new[] { "Id", "GroupId", "RequiredItemLevel", "Tier" },
                values: new object[,]
                {
                    { 287, 111, (short)1, (byte)9 },
                    { 288, 111, (short)4, (byte)8 },
                    { 289, 111, (short)7, (byte)7 },
                    { 290, 112, (short)1, (byte)9 },
                    { 291, 112, (short)4, (byte)8 },
                    { 292, 112, (short)7, (byte)7 },
                    { 293, 113, (short)1, (byte)9 },
                    { 294, 113, (short)4, (byte)8 },
                    { 295, 113, (short)7, (byte)7 },
                    { 296, 114, (short)1, (byte)9 },
                    { 297, 114, (short)4, (byte)8 },
                    { 298, 114, (short)7, (byte)7 },
                    { 299, 115, (short)1, (byte)9 },
                    { 300, 115, (short)4, (byte)8 },
                    { 301, 115, (short)7, (byte)7 },
                    { 302, 116, (short)1, (byte)9 },
                    { 303, 116, (short)4, (byte)8 },
                    { 304, 116, (short)7, (byte)7 },
                    { 305, 117, (short)1, (byte)9 },
                    { 306, 117, (short)4, (byte)8 },
                    { 307, 117, (short)7, (byte)7 },
                    { 308, 118, (short)1, (byte)9 },
                    { 309, 118, (short)4, (byte)8 },
                    { 310, 118, (short)7, (byte)7 },
                    { 311, 119, (short)1, (byte)9 },
                    { 312, 119, (short)4, (byte)8 },
                    { 313, 119, (short)7, (byte)7 },
                    { 314, 120, (short)1, (byte)9 },
                    { 315, 120, (short)4, (byte)8 },
                    { 316, 120, (short)7, (byte)7 },
                    { 317, 121, (short)1, (byte)9 },
                    { 318, 121, (short)4, (byte)8 },
                    { 319, 121, (short)7, (byte)7 },
                    { 320, 122, (short)1, (byte)9 },
                    { 321, 122, (short)4, (byte)8 },
                    { 322, 122, (short)7, (byte)7 },
                    { 323, 123, (short)1, (byte)9 },
                    { 324, 123, (short)4, (byte)8 },
                    { 325, 123, (short)7, (byte)7 },
                    { 326, 124, (short)1, (byte)9 },
                    { 327, 124, (short)4, (byte)8 },
                    { 328, 124, (short)7, (byte)7 },
                    { 329, 125, (short)1, (byte)9 },
                    { 330, 125, (short)4, (byte)8 },
                    { 331, 125, (short)7, (byte)7 },
                    { 332, 126, (short)1, (byte)9 },
                    { 333, 126, (short)4, (byte)8 },
                    { 334, 126, (short)7, (byte)7 },
                    { 335, 127, (short)1, (byte)9 },
                    { 336, 127, (short)4, (byte)8 },
                    { 337, 127, (short)7, (byte)7 },
                    { 338, 128, (short)1, (byte)9 },
                    { 339, 128, (short)4, (byte)8 },
                    { 340, 128, (short)7, (byte)7 },
                    { 341, 129, (short)1, (byte)9 },
                    { 342, 129, (short)4, (byte)8 },
                    { 343, 129, (short)7, (byte)7 },
                    { 344, 130, (short)1, (byte)9 },
                    { 345, 130, (short)4, (byte)8 },
                    { 346, 130, (short)7, (byte)7 },
                    { 347, 131, (short)1, (byte)9 },
                    { 348, 131, (short)4, (byte)8 },
                    { 349, 131, (short)7, (byte)7 },
                    { 350, 132, (short)1, (byte)9 },
                    { 351, 132, (short)4, (byte)8 },
                    { 352, 132, (short)7, (byte)7 },
                    { 353, 133, (short)1, (byte)9 },
                    { 354, 133, (short)4, (byte)8 },
                    { 355, 133, (short)7, (byte)7 },
                    { 356, 134, (short)1, (byte)9 },
                    { 357, 134, (short)4, (byte)8 },
                    { 358, 134, (short)7, (byte)7 },
                    { 359, 135, (short)1, (byte)9 },
                    { 360, 135, (short)4, (byte)8 },
                    { 361, 135, (short)7, (byte)7 },
                    { 362, 136, (short)1, (byte)9 },
                    { 363, 136, (short)4, (byte)8 },
                    { 364, 136, (short)7, (byte)7 }
                });

            migrationBuilder.InsertData(
                table: "ItemModifierStats",
                columns: new[] { "Index", "ModifierId", "Max", "Max2", "Min", "Min2", "Stat" },
                values: new object[,]
                {
                    { (byte)0, 287, 2, null, 1, null, (short)15 },
                    { (byte)0, 288, 3, null, 2, null, (short)15 },
                    { (byte)0, 289, 4, null, 3, null, (short)15 },
                    { (byte)0, 290, 2, null, 1, null, (short)16 },
                    { (byte)0, 291, 3, null, 2, null, (short)16 },
                    { (byte)0, 292, 4, null, 3, null, (short)16 },
                    { (byte)0, 293, 2, null, 1, null, (short)17 },
                    { (byte)0, 294, 3, null, 2, null, (short)17 },
                    { (byte)0, 295, 4, null, 3, null, (short)17 },
                    { (byte)0, 296, 2, null, 1, null, (short)18 },
                    { (byte)0, 297, 3, null, 2, null, (short)18 },
                    { (byte)0, 298, 4, null, 3, null, (short)18 },
                    { (byte)0, 299, 8, null, 5, null, (short)41 },
                    { (byte)0, 300, 12, null, 9, null, (short)41 },
                    { (byte)0, 301, 16, null, 13, null, (short)41 },
                    { (byte)0, 302, 2, null, 1, null, (short)53 },
                    { (byte)0, 303, 3, null, 2, null, (short)53 },
                    { (byte)0, 304, 4, null, 3, null, (short)53 },
                    { (byte)0, 305, 1, 1, 1, 1, (short)45 },
                    { (byte)0, 306, 1, 2, 1, 2, (short)45 },
                    { (byte)0, 307, 1, 3, 1, 3, (short)45 },
                    { (byte)0, 308, 1, null, 1, null, (short)1 },
                    { (byte)0, 309, 2, null, 2, null, (short)1 },
                    { (byte)0, 310, 3, null, 3, null, (short)1 },
                    { (byte)0, 311, 1, null, 1, null, (short)0 },
                    { (byte)0, 312, 2, null, 2, null, (short)0 },
                    { (byte)0, 313, 3, null, 3, null, (short)0 },
                    { (byte)0, 314, 3, null, 2, null, (short)32 },
                    { (byte)0, 315, 5, null, 4, null, (short)32 },
                    { (byte)0, 316, 6, null, 5, null, (short)32 },
                    { (byte)0, 317, 1, null, 1, null, (short)2 },
                    { (byte)0, 318, 2, null, 2, null, (short)2 },
                    { (byte)0, 319, 3, null, 3, null, (short)2 },
                    { (byte)0, 320, 8, null, 5, null, (short)42 },
                    { (byte)0, 321, 12, null, 9, null, (short)42 },
                    { (byte)0, 322, 16, null, 13, null, (short)42 },
                    { (byte)0, 323, 3, null, 2, null, (short)28 },
                    { (byte)0, 324, 4, null, 3, null, (short)28 },
                    { (byte)0, 325, 6, null, 5, null, (short)28 },
                    { (byte)0, 326, 2, null, 2, null, (short)13 },
                    { (byte)0, 327, 3, null, 3, null, (short)13 },
                    { (byte)0, 328, 4, null, 4, null, (short)13 },
                    { (byte)0, 329, 1, null, 1, null, (short)34 },
                    { (byte)0, 330, 2, null, 2, null, (short)34 },
                    { (byte)0, 331, 3, null, 3, null, (short)34 },
                    { (byte)0, 332, 1, null, 1, null, (short)3 },
                    { (byte)0, 333, 2, null, 2, null, (short)3 },
                    { (byte)0, 334, 3, null, 3, null, (short)3 },
                    { (byte)0, 335, 3, null, 2, null, (short)26 },
                    { (byte)0, 336, 4, null, 3, null, (short)26 },
                    { (byte)0, 337, 6, null, 5, null, (short)26 },
                    { (byte)0, 338, 1, null, 1, null, (short)33 },
                    { (byte)0, 339, 2, null, 2, null, (short)33 },
                    { (byte)0, 340, 3, null, 3, null, (short)33 },
                    { (byte)0, 341, 15, null, 10, null, (short)11 },
                    { (byte)0, 342, 25, null, 20, null, (short)11 },
                    { (byte)0, 343, 40, null, 30, null, (short)11 },
                    { (byte)0, 344, 2, null, 1, null, (short)10 },
                    { (byte)0, 345, 4, null, 3, null, (short)10 },
                    { (byte)0, 346, 5, null, 5, null, (short)10 },
                    { (byte)0, 347, 3, null, 2, null, (short)20 },
                    { (byte)0, 348, 4, null, 3, null, (short)20 },
                    { (byte)0, 349, 6, null, 5, null, (short)20 },
                    { (byte)0, 350, 1, 1, 1, 1, (short)48 },
                    { (byte)0, 351, 1, 2, 1, 2, (short)48 },
                    { (byte)0, 352, 1, 3, 1, 3, (short)48 },
                    { (byte)0, 353, 1, 1, 1, 1, (short)50 },
                    { (byte)0, 354, 1, 2, 1, 2, (short)50 },
                    { (byte)0, 355, 1, 3, 1, 3, (short)50 },
                    { (byte)0, 356, 1, 1, 1, 1, (short)51 },
                    { (byte)0, 357, 1, 2, 1, 2, (short)51 },
                    { (byte)0, 358, 1, 3, 1, 3, (short)51 },
                    { (byte)0, 359, 1, null, 1, null, (short)30 },
                    { (byte)0, 360, 2, null, 2, null, (short)30 },
                    { (byte)0, 361, 3, null, 3, null, (short)30 },
                    { (byte)0, 362, 3, null, 2, null, (short)31 },
                    { (byte)0, 363, 4, null, 3, null, (short)31 },
                    { (byte)0, 364, 6, null, 5, null, (short)31 }
                });

            migrationBuilder.InsertData(
                table: "ItemTemplateModifiers",
                columns: new[] { "Index", "TemplateId", "ModifierId" },
                values: new object[,]
                {
                    { (byte)0, 5m, 336 },
                    { (byte)0, 6m, 324 },
                    { (byte)0, 7m, 315 },
                    { (byte)0, 8m, 348 },
                    { (byte)0, 12m, 288 },
                    { (byte)0, 13m, 303 },
                    { (byte)0, 14m, 309 },
                    { (byte)0, 15m, 306 },
                    { (byte)0, 16m, 312 },
                    { (byte)0, 17m, 330 },
                    { (byte)0, 18m, 336 },
                    { (byte)0, 19m, 342 },
                    { (byte)0, 20m, 339 },
                    { (byte)0, 21m, 291 },
                    { (byte)0, 22m, 315 },
                    { (byte)0, 23m, 321 },
                    { (byte)0, 24m, 294 },
                    { (byte)0, 25m, 324 },
                    { (byte)0, 26m, 327 },
                    { (byte)0, 32m, 314 },
                    { (byte)0, 33m, 335 },
                    { (byte)0, 34m, 323 },
                    { (byte)0, 35m, 347 },
                    { (byte)0, 36m, 287 },
                    { (byte)0, 37m, 302 },
                    { (byte)0, 38m, 308 },
                    { (byte)0, 39m, 305 },
                    { (byte)0, 40m, 311 },
                    { (byte)0, 41m, 329 },
                    { (byte)0, 42m, 335 },
                    { (byte)0, 43m, 341 },
                    { (byte)0, 44m, 338 },
                    { (byte)0, 45m, 290 },
                    { (byte)0, 46m, 314 },
                    { (byte)0, 47m, 320 },
                    { (byte)0, 48m, 293 },
                    { (byte)0, 49m, 323 },
                    { (byte)0, 50m, 326 },
                    { (byte)0, 68m, 344 },
                    { (byte)0, 69m, 308 },
                    { (byte)0, 70m, 344 },
                    { (byte)0, 71m, 345 },
                    { (byte)0, 72m, 346 },
                    { (byte)0, 73m, 335 },
                    { (byte)0, 74m, 336 },
                    { (byte)0, 75m, 337 },
                    { (byte)0, 76m, 299 },
                    { (byte)0, 77m, 317 },
                    { (byte)0, 78m, 332 },
                    { (byte)0, 79m, 300 },
                    { (byte)0, 80m, 318 },
                    { (byte)0, 81m, 333 },
                    { (byte)0, 82m, 301 },
                    { (byte)0, 83m, 319 },
                    { (byte)0, 84m, 334 },
                    { (byte)0, 85m, 289 },
                    { (byte)0, 86m, 304 },
                    { (byte)0, 87m, 310 },
                    { (byte)0, 88m, 307 },
                    { (byte)0, 89m, 313 },
                    { (byte)0, 90m, 316 },
                    { (byte)0, 91m, 322 },
                    { (byte)0, 92m, 295 },
                    { (byte)0, 93m, 325 },
                    { (byte)0, 94m, 328 },
                    { (byte)0, 95m, 331 },
                    { (byte)0, 96m, 337 },
                    { (byte)0, 97m, 343 },
                    { (byte)0, 98m, 340 },
                    { (byte)0, 99m, 292 },
                    { (byte)0, 100m, 316 },
                    { (byte)0, 101m, 349 },
                    { (byte)0, 102m, 337 },
                    { (byte)0, 103m, 325 },
                    { (byte)0, 104m, 345 },
                    { (byte)0, 105m, 346 },
                    { (byte)0, 106m, 305 },
                    { (byte)0, 107m, 306 },
                    { (byte)0, 108m, 307 },
                    { (byte)0, 109m, 353 },
                    { (byte)0, 110m, 354 },
                    { (byte)0, 111m, 355 },
                    { (byte)0, 112m, 287 },
                    { (byte)0, 113m, 288 },
                    { (byte)0, 114m, 289 },
                    { (byte)0, 115m, 290 },
                    { (byte)0, 116m, 291 },
                    { (byte)0, 117m, 292 },
                    { (byte)0, 118m, 293 },
                    { (byte)0, 119m, 294 },
                    { (byte)0, 120m, 295 },
                    { (byte)0, 121m, 296 },
                    { (byte)0, 122m, 297 },
                    { (byte)0, 123m, 298 },
                    { (byte)0, 124m, 350 },
                    { (byte)0, 125m, 351 },
                    { (byte)0, 126m, 352 },
                    { (byte)0, 127m, 356 },
                    { (byte)0, 128m, 357 },
                    { (byte)0, 129m, 358 },
                    { (byte)0, 130m, 309 },
                    { (byte)0, 131m, 310 },
                    { (byte)0, 132m, 317 },
                    { (byte)0, 133m, 318 },
                    { (byte)0, 134m, 319 },
                    { (byte)0, 135m, 332 },
                    { (byte)0, 136m, 333 },
                    { (byte)0, 137m, 334 },
                    { (byte)0, 138m, 359 },
                    { (byte)0, 139m, 360 },
                    { (byte)0, 140m, 361 },
                    { (byte)0, 141m, 362 },
                    { (byte)0, 142m, 363 },
                    { (byte)0, 143m, 364 },
                    { (byte)0, 144m, 314 },
                    { (byte)0, 145m, 315 },
                    { (byte)0, 146m, 316 },
                    { (byte)0, 147m, 329 },
                    { (byte)0, 148m, 330 },
                    { (byte)0, 149m, 331 },
                    { (byte)0, 150m, 362 },
                    { (byte)0, 151m, 329 }
                });

            // The uniques name their bases only once those bases exist (EF emits UpdateData before InsertData).
            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                column: "BaseTemplateId",
                value: 144m);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                column: "BaseTemplateId",
                value: 112m);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                column: "BaseTemplateId",
                value: 125m);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 66m,
                column: "BaseTemplateId",
                value: 150m);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 67m,
                column: "BaseTemplateId",
                value: 151m);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTemplates_BaseTemplateId",
                table: "ItemTemplates",
                column: "BaseTemplateId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_BaseTemplate",
                table: "ItemTemplates",
                sql: "(\"Rarity\" = 3) = (\"BaseTemplateId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemTemplates_ItemTemplates_BaseTemplateId",
                table: "ItemTemplates",
                column: "BaseTemplateId",
                principalTable: "ItemTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemTemplates_ItemTemplates_BaseTemplateId",
                table: "ItemTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ItemTemplates_BaseTemplateId",
                table: "ItemTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_BaseTemplate",
                table: "ItemTemplates");

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 287 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 288 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 289 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 290 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 291 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 292 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 293 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 294 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 295 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 296 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 297 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 298 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 299 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 300 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 301 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 302 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 303 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 304 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 305 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 306 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 307 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 308 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 309 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 310 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 311 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 312 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 313 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 314 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 315 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 316 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 317 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 318 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 319 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 320 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 321 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 322 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 323 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 324 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 325 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 326 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 327 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 328 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 329 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 330 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 331 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 332 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 333 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 334 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 335 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 336 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 337 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 338 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 339 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 340 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 341 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 342 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 343 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 344 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 345 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 346 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 347 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 348 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 349 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 350 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 351 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 352 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 353 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 354 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 355 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 356 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 357 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 358 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 359 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 360 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 361 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 362 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 363 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 364 });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 5m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 6m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 7m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 8m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 12m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 13m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 14m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 15m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 16m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 17m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 18m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 19m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 20m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 21m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 22m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 23m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 24m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 25m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 26m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 32m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 33m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 34m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 35m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 36m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 37m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 38m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 39m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 40m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 41m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 42m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 43m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 44m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 45m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 46m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 47m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 48m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 49m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 50m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 68m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 69m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 70m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 71m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 72m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 73m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 74m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 75m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 76m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 77m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 78m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 79m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 80m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 81m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 82m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 83m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 84m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 85m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 86m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 87m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 88m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 89m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 90m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 91m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 92m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 93m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 94m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 95m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 96m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 97m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 98m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 99m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 100m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 101m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 102m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 103m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 104m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 105m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 106m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 107m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 108m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 109m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 110m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 111m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 112m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 113m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 114m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 115m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 116m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 117m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 118m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 119m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 120m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 121m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 122m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 123m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 124m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 125m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 126m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 127m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 128m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 129m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 130m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 131m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 132m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 133m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 134m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 135m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 136m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 137m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 138m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 139m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 140m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 141m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 142m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 143m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 144m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 145m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 146m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 147m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 148m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 149m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 150m });

            migrationBuilder.DeleteData(
                table: "ItemTemplateModifiers",
                keyColumns: new[] { "Index", "TemplateId" },
                keyValues: new object[] { (byte)0, 151m });

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 287);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 288);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 289);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 290);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 291);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 292);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 293);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 294);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 295);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 296);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 297);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 298);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 299);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 300);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 301);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 302);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 303);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 304);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 305);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 306);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 307);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 308);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 309);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 310);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 311);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 312);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 313);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 314);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 315);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 316);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 317);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 318);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 319);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 320);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 321);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 322);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 323);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 324);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 325);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 326);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 327);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 328);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 329);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 330);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 331);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 332);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 333);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 334);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 335);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 336);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 337);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 338);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 339);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 340);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 341);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 342);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 343);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 344);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 345);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 346);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 347);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 348);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 349);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 350);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 351);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 352);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 353);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 354);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 355);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 356);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 357);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 358);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 359);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 360);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 361);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 362);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 363);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 364);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 104m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 105m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 106m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 107m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 108m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 109m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 110m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 111m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 112m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 113m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 114m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 115m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 116m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 117m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 118m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 119m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 120m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 121m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 122m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 123m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 124m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 125m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 126m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 127m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 128m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 129m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 130m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 131m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 132m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 133m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 134m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 135m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 136m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 137m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 138m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 139m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 140m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 141m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 142m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 143m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 144m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 145m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 146m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 147m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 148m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 149m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 150m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 151m);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 111);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 112);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 113);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 114);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 115);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 116);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 117);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 118);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 119);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 120);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 121);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 122);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 123);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 124);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 125);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 126);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 127);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 128);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 129);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 130);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 131);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 132);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 133);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 134);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 135);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 136);

            migrationBuilder.DropColumn(
                name: "BaseTemplateId",
                table: "ItemTemplates");

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 219 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 219 },
                column: "Max",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 219 },
                column: "Max",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 220 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 24, 16 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 220 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 220 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 221 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 36, 24 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 221 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 2, 5, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 221 },
                columns: new[] { "Max", "Stat" },
                values: new object[] { 5, (short)7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 222 },
                columns: new[] { "Max", "Min", "Stat" },
                values: new object[] { 30, 20, (short)10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 222 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 18, 12 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 223 },
                columns: new[] { "Max", "Min", "Stat" },
                values: new object[] { 24, 16, (short)10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 223 },
                column: "Stat",
                value: (short)12);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)2, 223 },
                column: "Stat",
                value: (short)33);

            migrationBuilder.InsertData(
                table: "ItemModifierStats",
                columns: new[] { "Index", "ModifierId", "Max", "Max2", "Min", "Min2", "Stat" },
                values: new object[] { (byte)2, 222, 12, null, 8, null, (short)18 });
        }
    }
}
