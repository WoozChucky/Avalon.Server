using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceItemsWithBases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reordered by hand (#1179): EF emits UpdateData before InsertData and deletes the retired items before the
            // rows that name them. The order is: the new columns; the 38 new items; the kept items' new values; loot
            // table 14; the old loot entries, vendor rows and quest rewards and choices out, the kept loot entries
            // re-pointed and the replacements in; only then the 11 retired items, and ItemPower last. The checks go on
            // once every row holds them.
            migrationBuilder.AlterColumn<byte>(
                name: "Rarity",
                table: "ItemTemplates",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<byte>(
                name: "BaseKind",
                table: "ItemTemplates",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAnyAttribute",
                table: "ItemTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "AttackTimeMs", "BaseKind", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "MaxStackSize", "Name", "Rarity", "RequiredAgility", "RequiredIntellect", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass", "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[,]
                {
                    { 66m, new byte[0], null, (byte)27, 800L, 2, null, null, null, null, null, null, 66L, 0, 1L, "Bramble Mother's Seed", (byte)3, null, null, 5, null, false, 200L, 11, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 210, null, null, null, null, null },
                    { 67m, new byte[0], null, (byte)27, 400L, 2, null, null, null, null, null, null, 67L, 0, 1L, "Tuskroot Idol", (byte)3, null, null, 4, null, false, 100L, 11, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 210, null, null, null, null, null },
                    { 68m, new byte[0], null, (byte)26, 200L, 2, null, null, null, null, null, null, 68L, 0, 1L, "Copper Band", (byte)0, null, null, 1, null, false, 50L, 7, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 207, null, null, null, null, null },
                    { 69m, new byte[0], null, (byte)25, 200L, 2, null, null, null, null, null, null, 69L, 0, 1L, "Bone Charm", (byte)0, null, null, 1, null, false, 50L, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 206, null, null, null, null, null },
                    { 70m, new byte[0], null, (byte)23, 120L, 2, null, null, null, null, null, null, 70L, 0, 1L, "Oak Buckler", (byte)0, null, null, 1, null, false, 30L, 10, 4, null, 5, null, null, null, null, null, null, null, 6L, null, 5L, null, null, null, null, null, null, null, 200, null, null, null, null, null },
                    { 71m, new byte[0], null, (byte)23, 240L, 2, null, null, null, null, null, null, 71L, 0, 1L, "Barkplate Shield", (byte)0, null, null, 5, 30, false, 60L, 10, 4, null, 5, null, null, null, null, null, null, null, 14L, null, 7L, null, null, null, null, null, null, null, 200, null, null, null, null, null },
                    { 72m, new byte[0], null, (byte)23, 480L, 2, null, null, null, null, null, null, 72L, 0, 1L, "Ironroot Kite Shield", (byte)0, null, null, 10, 45, false, 120L, 10, 4, null, 5, null, null, null, null, null, null, null, 24L, null, 10L, null, null, null, null, null, null, null, 200, null, null, null, null, null },
                    { 73m, new byte[0], null, (byte)24, 120L, 2, null, null, null, null, null, null, 73L, 0, 1L, "Ash Focus", (byte)0, null, null, 1, null, false, 30L, 10, 14, null, 26, null, null, null, null, null, null, null, 8L, null, 5L, null, null, null, null, null, null, null, 209, null, null, null, null, null },
                    { 74m, new byte[0], null, (byte)24, 240L, 2, null, null, null, null, null, null, 74L, 0, 1L, "Thornwood Focus", (byte)0, null, 30, 5, null, false, 60L, 10, 14, null, 26, null, null, null, null, null, null, null, 18L, null, 8L, null, null, null, null, null, null, null, 209, null, null, null, null, null },
                    { 75m, new byte[0], null, (byte)24, 480L, 2, null, null, null, null, null, null, 75L, 0, 1L, "Gloamwood Orb", (byte)0, null, 45, 10, null, false, 120L, 10, 14, null, 26, null, null, null, null, null, null, null, 30L, null, 12L, null, null, null, null, null, null, null, 209, null, null, null, null, null },
                    { 76m, new byte[0], null, (byte)2, 50L, 2, null, null, null, null, null, null, 76L, 0, 1L, "Ironbound Pauldrons", (byte)0, null, null, 1, null, false, 12L, 2, 4, null, null, null, null, null, null, null, null, null, 4L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 77m, new byte[0], null, (byte)8, 50L, 2, null, null, null, null, null, null, 77L, 0, 1L, "Hide Mantle", (byte)0, null, null, 1, null, false, 12L, 2, 6, null, null, null, null, null, null, null, null, null, 20L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 78m, new byte[0], null, (byte)14, 50L, 2, null, null, null, null, null, null, 78L, 0, 1L, "Linen Shoulderwraps", (byte)0, null, null, 1, null, false, 12L, 2, 14, null, null, null, null, null, null, null, null, null, 6L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 79m, new byte[0], null, (byte)2, 100L, 2, null, null, null, null, null, null, 79L, 0, 1L, "Barkplate Pauldrons", (byte)0, null, null, 5, 30, false, 25L, 2, 4, null, null, null, null, null, null, null, null, null, 9L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 80m, new byte[0], null, (byte)8, 100L, 2, null, null, null, null, null, null, 80L, 0, 1L, "Fernstalker Mantle", (byte)0, 30, null, 5, null, false, 25L, 2, 6, null, null, null, null, null, null, null, null, null, 45L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 81m, new byte[0], null, (byte)14, 100L, 2, null, null, null, null, null, null, 81L, 0, 1L, "Mossweave Shoulderwraps", (byte)0, null, 30, 5, null, false, 25L, 2, 14, null, null, null, null, null, null, null, null, null, 13L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 82m, new byte[0], null, (byte)2, 200L, 2, null, null, null, null, null, null, 82L, 0, 1L, "Ironroot Pauldrons", (byte)0, null, null, 10, 45, false, 50L, 2, 4, null, null, null, null, null, null, null, null, null, 16L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 83m, new byte[0], null, (byte)8, 200L, 2, null, null, null, null, null, null, 83L, 0, 1L, "Gloamhide Mantle", (byte)0, 45, null, 10, null, false, 50L, 2, 6, null, null, null, null, null, null, null, null, null, 80L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 84m, new byte[0], null, (byte)14, 200L, 2, null, null, null, null, null, null, 84L, 0, 1L, "Duskweave Shoulderwraps", (byte)0, null, 45, 10, null, false, 50L, 2, 14, null, null, null, null, null, null, null, null, null, 23L, null, null, null, null, null, null, null, null, null, 208, null, null, null, null, null },
                    { 85m, new byte[0], null, (byte)1, 240L, 2, null, null, null, null, null, null, 85L, 0, 1L, "Ironroot Helm", (byte)0, null, null, 10, 45, false, 60L, 0, 4, null, null, null, null, null, null, null, null, null, 19L, null, null, null, null, null, null, null, null, null, 201, null, null, null, null, null },
                    { 86m, new byte[0], null, (byte)3, 400L, 2, null, null, null, null, null, null, 86L, 0, 1L, "Ironroot Chestguard", (byte)0, null, null, 10, 45, false, 100L, 3, 4, null, null, null, null, null, null, null, null, null, 32L, null, null, null, null, null, null, null, null, null, 202, null, null, null, null, null },
                    { 87m, new byte[0], null, (byte)5, 320L, 2, null, null, null, null, null, null, 87L, 0, 1L, "Ironroot Legguards", (byte)0, null, null, 10, 45, false, 80L, 5, 4, null, null, null, null, null, null, null, null, null, 26L, null, null, null, null, null, null, null, null, null, 203, null, null, null, null, null },
                    { 88m, new byte[0], null, (byte)4, 160L, 2, null, null, null, null, null, null, 88L, 0, 1L, "Ironroot Gauntlets", (byte)0, null, null, 10, 45, false, 40L, 4, 4, null, null, null, null, null, null, null, null, null, 13L, null, null, null, null, null, null, null, null, null, 205, null, null, null, null, null },
                    { 89m, new byte[0], null, (byte)6, 160L, 2, null, null, null, null, null, null, 89L, 0, 1L, "Ironroot Boots", (byte)0, null, null, 10, 45, false, 40L, 6, 4, null, null, null, null, null, null, null, null, null, 13L, null, null, null, null, null, null, null, null, null, 204, null, null, null, null, null },
                    { 90m, new byte[0], null, (byte)7, 240L, 2, null, null, null, null, null, null, 90L, 0, 1L, "Gloamhide Cap", (byte)0, 45, null, 10, null, false, 60L, 0, 6, null, null, null, null, null, null, null, null, null, 96L, null, null, null, null, null, null, null, null, null, 201, null, null, null, null, null },
                    { 91m, new byte[0], null, (byte)9, 400L, 2, null, null, null, null, null, null, 91L, 0, 1L, "Gloamhide Jerkin", (byte)0, 45, null, 10, null, false, 100L, 3, 6, null, null, null, null, null, null, null, null, null, 160L, null, null, null, null, null, null, null, null, null, 202, null, null, null, null, null },
                    { 92m, new byte[0], null, (byte)11, 320L, 2, null, null, null, null, null, null, 92L, 0, 1L, "Gloamhide Breeches", (byte)0, 45, null, 10, null, false, 80L, 5, 6, null, null, null, null, null, null, null, null, null, 128L, null, null, null, null, null, null, null, null, null, 203, null, null, null, null, null },
                    { 93m, new byte[0], null, (byte)10, 160L, 2, null, null, null, null, null, null, 93L, 0, 1L, "Gloamhide Grips", (byte)0, 45, null, 10, null, false, 40L, 4, 6, null, null, null, null, null, null, null, null, null, 64L, null, null, null, null, null, null, null, null, null, 205, null, null, null, null, null },
                    { 94m, new byte[0], null, (byte)12, 160L, 2, null, null, null, null, null, null, 94L, 0, 1L, "Gloamhide Boots", (byte)0, 45, null, 10, null, false, 40L, 6, 6, null, null, null, null, null, null, null, null, null, 64L, null, null, null, null, null, null, null, null, null, 204, null, null, null, null, null },
                    { 95m, new byte[0], null, (byte)13, 240L, 2, null, null, null, null, null, null, 95L, 0, 1L, "Duskweave Hood", (byte)0, null, 45, 10, null, false, 60L, 0, 14, null, null, null, null, null, null, null, null, null, 27L, null, null, null, null, null, null, null, null, null, 201, null, null, null, null, null },
                    { 96m, new byte[0], null, (byte)15, 400L, 2, null, null, null, null, null, null, 96L, 0, 1L, "Duskweave Robe", (byte)0, null, 45, 10, null, false, 100L, 3, 14, null, null, null, null, null, null, null, null, null, 45L, null, null, null, null, null, null, null, null, null, 202, null, null, null, null, null },
                    { 97m, new byte[0], null, (byte)17, 320L, 2, null, null, null, null, null, null, 97L, 0, 1L, "Duskweave Leggings", (byte)0, null, 45, 10, null, false, 80L, 5, 14, null, null, null, null, null, null, null, null, null, 36L, null, null, null, null, null, null, null, null, null, 203, null, null, null, null, null },
                    { 98m, new byte[0], null, (byte)16, 160L, 2, null, null, null, null, null, null, 98L, 0, 1L, "Duskweave Gloves", (byte)0, null, 45, 10, null, false, 40L, 4, 14, null, null, null, null, null, null, null, null, null, 18L, null, null, null, null, null, null, null, null, null, 205, null, null, null, null, null },
                    { 99m, new byte[0], null, (byte)18, 160L, 2, null, null, null, null, null, null, 99L, 0, 1L, "Duskweave Slippers", (byte)0, null, 45, 10, null, false, 40L, 6, 14, null, null, null, null, null, null, null, null, null, 18L, null, null, null, null, null, null, null, null, null, 204, null, null, null, null, null },
                    { 100m, new byte[0], 792L, (byte)19, 480L, 1, 17L, null, 11L, null, 0, null, 100L, 0, 1L, "Ironroot Sword", (byte)0, null, null, 10, 45, false, 120L, 9, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 100, null, null, null, null, null },
                    { 101m, new byte[0], 784L, (byte)20, 480L, 1, 17L, null, 11L, null, 0, null, 101L, 0, 1L, "Ironroot Mace", (byte)0, null, 45, 10, 45, true, 120L, 9, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 100, null, null, null, null, null },
                    { 102m, new byte[0], 800L, (byte)21, 480L, 1, 17L, null, 11L, null, 0, null, 102L, 0, 1L, "Gloamwood Staff", (byte)0, null, 45, 10, null, false, 120L, 9, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 101, null, null, null, null, null },
                    { 103m, new byte[0], 801L, (byte)22, 480L, 1, 17L, null, 11L, null, 0, null, 103L, 0, 1L, "Duskstring Bow", (byte)0, 45, null, 10, null, false, 120L, 9, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 102, null, null, null, null, null }
                });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "DamageMax1", "DamageMin1", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)21, 240L, 11L, 7L, (byte)0, 30, 5, false, 60L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "DamageMax1", "DamageMin1", "Rarity", "RequiredAgility", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)22, 240L, 11L, 7L, (byte)0, 30, 5, false, 60L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "DamageMax1", "DamageMin1", "Rarity", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)19, 240L, 11L, 7L, (byte)0, 5, 30, false, 60L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)20, 240L, (byte)0, 30, 5, 30, true, 60L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)1, 120L, (byte)0, 5, 30, false, 30L, 4, null, null, 11L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)3, (byte)0, 5, 30, false, 4, null, null, 18L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)5, 160L, (byte)0, 5, 30, false, 40L, 4, null, null, 14L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)4, 80L, (byte)0, 5, 30, false, 20L, 4, null, null, 7L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredLevel", "RequiredStrength", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)6, 80L, (byte)0, 5, 30, false, 20L, 4, null, null, 7L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)13, 120L, (byte)0, 30, 5, false, 30L, 14, null, 16L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)15, (byte)0, 30, 5, false, 14, null, 26L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)17, 160L, (byte)0, 30, 5, false, 40L, 14, null, 21L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)16, 80L, (byte)0, 30, 5, false, 20L, 14, null, 10L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)18, 80L, (byte)0, 30, 5, false, 20L, 14, null, 10L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredAgility", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)7, 120L, (byte)0, 30, 5, false, 30L, 6, null, 54L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiredAgility", "RequiredLevel", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)9, (byte)0, 30, 5, false, 6, null, 90L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredAgility", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)11, 160L, (byte)0, 30, 5, false, 40L, 6, null, 72L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredAgility", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)10, 80L, (byte)0, 30, 5, false, 20L, 6, null, 36L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                columns: new[] { "AllowedClasses", "BaseKind", "BuyPrice", "Rarity", "RequiredAgility", "RequiredLevel", "RequiresAnyAttribute", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)12, 80L, (byte)0, 30, 5, false, 20L, 6, null, 36L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)19, (byte)0, false, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)21, (byte)0, false, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)22, (byte)0, false, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatValue1" },
                values: new object[] { new byte[0], (byte)20, (byte)0, true, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)1, (byte)0, false, 4, null, null, 5L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)3, (byte)0, false, 4, null, null, 8L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)5, (byte)0, false, 4, null, null, 6L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)4, (byte)0, false, 4, null, null, 3L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[0], (byte)6, (byte)0, false, 4, null, null, 3L, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)13, (byte)0, false, 14, null, 7L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)15, (byte)0, false, 14, null, 12L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)17, (byte)0, false, 14, null, 10L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)16, (byte)0, false, 14, null, 5L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)18, (byte)0, false, 14, null, 5L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)7, (byte)0, false, 6, null, 24L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)9, (byte)0, false, 6, null, 40L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)11, (byte)0, false, 6, null, 32L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)10, (byte)0, false, 6, null, 16L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                columns: new[] { "AllowedClasses", "BaseKind", "Rarity", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[0], (byte)12, (byte)0, false, 6, null, 16L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                columns: new[] { "BaseKind", "Rarity", "RequiredLevel", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { (byte)25, (byte)3, 3, false, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                columns: new[] { "BaseKind", "Rarity", "RequiresAnyAttribute" },
                values: new object[] { null, (byte)0, false });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                columns: new[] { "BaseKind", "Rarity", "RequiredLevel", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { (byte)26, (byte)3, 4, false, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                columns: new[] { "BaseKind", "Rarity", "RequiredLevel", "RequiresAnyAttribute", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { (byte)26, (byte)3, 5, false, null, null, null, null });

            migrationBuilder.InsertData(
                table: "LootTables",
                columns: new[] { "Id", "Name" },
                values: new object[] { 14, "Forest jewellery" });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 19 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 20 });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 5m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 6m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 7m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 8m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 13m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 18m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 23m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 28m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 58m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 64m, 7L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 65m, 8L });

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 1 },
                column: "ItemTemplateId",
                value: 32m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 2 },
                column: "ItemTemplateId",
                value: 35m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 3 },
                column: "ItemTemplateId",
                value: 33m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 4 },
                column: "ItemTemplateId",
                value: 34m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 1 },
                column: "ItemTemplateId",
                value: 36m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 2 },
                column: "ItemTemplateId",
                value: 37m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 3 },
                column: "ItemTemplateId",
                value: 38m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 4 },
                column: "ItemTemplateId",
                value: 39m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 5 },
                column: "ItemTemplateId",
                value: 40m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 6 },
                column: "ItemTemplateId",
                value: 76m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 7 },
                column: "ItemTemplateId",
                value: 46m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 8 },
                column: "ItemTemplateId",
                value: 47m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 9 },
                column: "ItemTemplateId",
                value: 48m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 10 },
                column: "ItemTemplateId",
                value: 49m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 11 },
                column: "ItemTemplateId",
                value: 50m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 12 },
                column: "ItemTemplateId",
                value: 77m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 13 },
                column: "ItemTemplateId",
                value: 41m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 14 },
                column: "ItemTemplateId",
                value: 42m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 15 },
                column: "ItemTemplateId",
                value: 43m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 16 },
                column: "ItemTemplateId",
                value: 44m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 17 },
                column: "ItemTemplateId",
                value: 45m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 18 },
                column: "ItemTemplateId",
                value: 78m);

            migrationBuilder.InsertData(
                table: "QuestItemChoices",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[,]
                {
                    { 32m, 3L, 1L },
                    { 33m, 3L, 1L },
                    { 34m, 3L, 1L },
                    { 35m, 3L, 1L },
                    { 37m, 11L, 1L },
                    { 42m, 11L, 1L },
                    { 47m, 11L, 1L }
                });

            migrationBuilder.InsertData(
                table: "VendorStocks",
                columns: new[] { "Id", "CreatureTemplateId", "ItemTemplateId", "MaxStock", "PriceOverride", "RequiredQuestId", "RequiredQuestState", "RestockSeconds", "Sequence" },
                values: new object[,]
                {
                    { 32, 12m, 32m, null, null, null, null, null, 1L },
                    { 33, 12m, 35m, null, null, null, null, null, 2L },
                    { 34, 12m, 33m, null, null, null, null, null, 3L },
                    { 35, 12m, 34m, null, null, null, null, null, 4L },
                    { 38, 13m, 36m, null, null, null, null, null, 1L },
                    { 39, 13m, 37m, 2L, null, null, null, 1800L, 2L },
                    { 40, 13m, 38m, null, null, null, null, null, 3L },
                    { 41, 13m, 39m, null, null, null, null, null, 4L },
                    { 42, 13m, 40m, null, null, null, null, null, 5L },
                    { 44, 13m, 46m, null, null, null, null, null, 7L },
                    { 45, 13m, 47m, 2L, null, null, null, 1800L, 8L },
                    { 46, 13m, 48m, null, null, null, null, null, 9L },
                    { 47, 13m, 49m, null, null, null, null, null, 10L },
                    { 48, 13m, 50m, null, null, null, null, null, 11L },
                    { 50, 13m, 41m, null, null, null, null, null, 13L },
                    { 51, 13m, 42m, 2L, null, null, null, 1800L, 14L },
                    { 52, 13m, 43m, null, null, null, null, null, 15L },
                    { 53, 13m, 44m, null, null, null, null, null, 16L },
                    { 54, 13m, 45m, null, null, null, null, null, 17L }
                });

            migrationBuilder.InsertData(
                table: "LootTableEntries",
                columns: new[] { "LootTableId", "Sequence", "Chance", "GroupId", "ItemTemplateId", "MaxCount", "MinCount", "ReferenceTableId" },
                values: new object[,]
                {
                    { 2, 7, 100f, null, null, 1, 1, 14 },
                    { 3, 7, 100f, null, null, 1, 1, 14 },
                    { 4, 7, 100f, null, null, 1, 1, 14 },
                    { 5, 7, 100f, null, null, 1, 1, 14 },
                    { 6, 7, 100f, null, null, 1, 1, 14 },
                    { 7, 7, 100f, null, null, 1, 1, 14 },
                    { 8, 7, 100f, null, null, 1, 1, 14 },
                    { 9, 5, 25f, 1, 70m, 1, 1, null },
                    { 9, 6, 25f, 1, 73m, 1, 1, null },
                    { 12, 7, 100f, null, null, 1, 1, 14 },
                    { 14, 1, 2f, null, 68m, 1, 1, null },
                    { 14, 2, 2f, null, 69m, 1, 1, null }
                });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[,]
                {
                    { 69m, 3L, 1L },
                    { 68m, 7L, 1L },
                    { 68m, 8L, 1L }
                });

            migrationBuilder.InsertData(
                table: "VendorStocks",
                columns: new[] { "Id", "CreatureTemplateId", "ItemTemplateId", "MaxStock", "PriceOverride", "RequiredQuestId", "RequiredQuestState", "RestockSeconds", "Sequence" },
                values: new object[,]
                {
                    { 36, 12m, 70m, null, null, null, null, null, 5L },
                    { 37, 12m, 73m, null, null, null, null, null, 6L },
                    { 43, 13m, 76m, null, null, null, null, null, 6L },
                    { 49, 13m, 77m, null, null, null, null, null, 12L },
                    { 55, 13m, 78m, null, null, null, null, null, 18L }
                });

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 27m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 29m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 28m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 30m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 31m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 51m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 52m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 53m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 54m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 55m);

            migrationBuilder.DropColumn(
                name: "ItemPower",
                table: "ItemTemplates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_BaseKind",
                table: "ItemTemplates",
                sql: "\"BaseKind\" IS NULL OR \"BaseKind\" BETWEEN 1 AND 27");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_Rarity",
                table: "ItemTemplates",
                sql: "\"Rarity\" IN (0, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The same, reversed by hand: the checks off, Rarity widened and ItemPower back; the 11 retired items back and
            // the kept items' old values; the new references out, the kept loot entries pointed back and the old
            // references in; then the 38 new items and loot table 14 out, and the new columns last.
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_BaseKind",
                table: "ItemTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_Rarity",
                table: "ItemTemplates");

            migrationBuilder.AlterColumn<int>(
                name: "Rarity",
                table: "ItemTemplates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AddColumn<int>(
                name: "ItemPower",
                table: "ItemTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "AttackTimeMs", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredAgility", "RequiredIntellect", "RequiredLevel", "RequiredStrength", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass", "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[,]
                {
                    { 4m, new byte[] { 1 }, 792L, 100L, 1, 6L, null, 4L, null, 0, null, 4L, 256, 2, 1L, "Rusted Sword", 1, null, null, 1, null, 50L, 9, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 100, null, null, null, null, null },
                    { 27m, new byte[] { 4 }, null, 200L, 2, null, null, null, null, null, null, 27L, 0, 3, 1L, "Dewleaf Circlet", 2, null, null, 1, null, 50L, 0, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 201, null, null, null, null, null },
                    { 28m, new byte[] { 4 }, null, 200L, 2, null, null, null, null, null, null, 28L, 0, 3, 1L, "Dewleaf Vestments", 2, null, null, 1, null, 50L, 3, 3, null, 0, 4, null, null, null, null, null, null, 2L, null, 2L, 3L, null, null, null, null, null, null, 202, null, null, null, null, null },
                    { 29m, new byte[] { 4 }, null, 200L, 2, null, null, null, null, null, null, 29L, 0, 3, 1L, "Dewleaf Leggings", 2, null, null, 1, null, 50L, 5, 3, null, 0, 4, null, null, null, null, null, null, 2L, null, 1L, 2L, null, null, null, null, null, null, 203, null, null, null, null, null },
                    { 30m, new byte[] { 4 }, null, 200L, 2, null, null, null, null, null, null, 30L, 0, 3, 1L, "Dewleaf Handwraps", 2, null, null, 1, null, 50L, 4, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 205, null, null, null, null, null },
                    { 31m, new byte[] { 4 }, null, 200L, 2, null, null, null, null, null, null, 31L, 0, 3, 1L, "Dewleaf Sandals", 2, null, null, 1, null, 50L, 6, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 204, null, null, null, null, null },
                    { 51m, new byte[] { 4 }, null, 60L, 2, null, null, null, null, null, null, 51L, 0, 2, 1L, "Wool Circlet", 1, null, null, 1, null, 15L, 0, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 201, null, null, null, null, null },
                    { 52m, new byte[] { 4 }, null, 100L, 2, null, null, null, null, null, null, 52L, 0, 2, 1L, "Wool Vestments", 1, null, null, 1, null, 25L, 3, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 2L, null, null, null, null, null, null, 202, null, null, null, null, null },
                    { 53m, new byte[] { 4 }, null, 80L, 2, null, null, null, null, null, null, 53L, 0, 2, 1L, "Wool Leggings", 1, null, null, 1, null, 20L, 5, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 203, null, null, null, null, null },
                    { 54m, new byte[] { 4 }, null, 40L, 2, null, null, null, null, null, null, 54L, 0, 2, 1L, "Wool Handwraps", 1, null, null, 1, null, 10L, 4, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 205, null, null, null, null, null },
                    { 55m, new byte[] { 4 }, null, 40L, 2, null, null, null, null, null, null, 55L, 0, 2, 1L, "Wool Sandals", 1, null, null, 1, null, 10L, 6, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 204, null, null, null, null, null }
                });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "AllowedClasses", "BuyPrice", "DamageMax1", "DamageMin1", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 2 }, 200L, 14L, 9L, 3, 2, null, 1, 50L, 3, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "AllowedClasses", "BuyPrice", "DamageMax1", "DamageMin1", "ItemPower", "Rarity", "RequiredAgility", "RequiredLevel", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 3 }, 200L, 7L, 5L, 3, 2, null, 1, 50L, 2, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "AllowedClasses", "BuyPrice", "DamageMax1", "DamageMin1", "ItemPower", "Rarity", "RequiredLevel", "RequiredStrength", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 1 }, 200L, 7L, 5L, 3, 2, 1, null, 50L, 1, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "RequiredStrength", "SellPrice", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 4 }, 200L, 3, 2, null, 1, null, 50L, 3, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredLevel", "RequiredStrength", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 200L, 3, 2, 1, null, 50L, 1, 4, 0, 1L, 4L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "RequiredLevel", "RequiredStrength", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 3, 2, 1, null, 1, 4, 0, 2L, 8L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredLevel", "RequiredStrength", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 200L, 3, 2, 1, null, 50L, 1, 4, 0, 2L, 6L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredLevel", "RequiredStrength", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 200L, 3, 2, 1, null, 50L, 1, 4, 0, 1L, 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredLevel", "RequiredStrength", "SellPrice", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 200L, 3, 2, 1, null, 50L, 1, 4, 0, 1L, 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 200L, 3, 2, null, 1, 50L, 3, 4, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 3, 2, null, 1, 3, 4, 3L, 3L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 200L, 3, 2, null, 1, 50L, 3, 4, 3L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 200L, 3, 2, null, 1, 50L, 3, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredIntellect", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 200L, 3, 2, null, 1, 50L, 3, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredAgility", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 200L, 3, 2, null, 1, 50L, 2, 4, 2L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "RequiredAgility", "RequiredLevel", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 3, 2, null, 1, 2, 4, 3L, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredAgility", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 200L, 3, 2, null, 1, 50L, 2, 4, 3L, 4L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredAgility", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 200L, 3, 2, null, 1, 50L, 2, 4, 1L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                columns: new[] { "AllowedClasses", "BuyPrice", "ItemPower", "Rarity", "RequiredAgility", "RequiredLevel", "SellPrice", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 200L, 3, 2, null, 1, 50L, 2, 4, 1L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 1 }, 2, 1, 1, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 2 }, 2, 1, 3, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 3 }, 2, 1, 2, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatValue1" },
                values: new object[] { new byte[] { 4 }, 2, 1, 3, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 2, 1, 1, 4, 0, 1L, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 2, 1, 1, 4, 0, 1L, 5L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 2, 1, 1, 4, 0, 1L, 4L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 2, 1, 1, 4, 0, 1L, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatType3", "StatValue1", "StatValue2", "StatValue3" },
                values: new object[] { new byte[] { 1 }, 2, 1, 1, 4, 0, 1L, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 2, 1, 3, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 2, 1, 3, 4, 2L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 2, 1, 3, 4, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 2, 1, 3, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 2 }, 2, 1, 3, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 2, 1, 2, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 2, 1, 2, 4, 2L, 3L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 2, 1, 2, 4, 2L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 2, 1, 2, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                columns: new[] { "AllowedClasses", "ItemPower", "Rarity", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { new byte[] { 3 }, 2, 1, 2, 4, 1L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                columns: new[] { "ItemPower", "Rarity", "RequiredLevel", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 3, 2, 2, 0, 4, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                columns: new[] { "ItemPower", "Rarity" },
                values: new object[] { null, 1 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                columns: new[] { "ItemPower", "Rarity", "RequiredLevel", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 5, 2, 5, 0, 4, 3L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                columns: new[] { "ItemPower", "Rarity", "RequiredLevel", "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 7, 3, 7, 0, 4, 5L, 4L });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 2, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 3, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 4, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 5, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 6 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 14, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 14, 2 });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 32m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 33m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 34m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 35m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 37m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 42m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemChoices",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 47m, 11L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 69m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 68m, 7L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 68m, 8L });

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 1 },
                column: "ItemTemplateId",
                value: 7m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 2 },
                column: "ItemTemplateId",
                value: 5m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 3 },
                column: "ItemTemplateId",
                value: 6m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 4 },
                column: "ItemTemplateId",
                value: 8m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 1 },
                column: "ItemTemplateId",
                value: 12m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 2 },
                column: "ItemTemplateId",
                value: 13m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 3 },
                column: "ItemTemplateId",
                value: 14m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 4 },
                column: "ItemTemplateId",
                value: 15m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 5 },
                column: "ItemTemplateId",
                value: 16m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 6 },
                column: "ItemTemplateId",
                value: 17m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 7 },
                column: "ItemTemplateId",
                value: 18m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 8 },
                column: "ItemTemplateId",
                value: 19m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 9 },
                column: "ItemTemplateId",
                value: 20m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 10 },
                column: "ItemTemplateId",
                value: 21m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 11 },
                column: "ItemTemplateId",
                value: 22m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 12 },
                column: "ItemTemplateId",
                value: 23m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 13 },
                column: "ItemTemplateId",
                value: 24m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 14 },
                column: "ItemTemplateId",
                value: 25m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 15 },
                column: "ItemTemplateId",
                value: 26m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 16 },
                column: "ItemTemplateId",
                value: 27m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 17 },
                column: "ItemTemplateId",
                value: 28m);

            migrationBuilder.UpdateData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 18 },
                column: "ItemTemplateId",
                value: 29m);

            migrationBuilder.InsertData(
                table: "QuestItemChoices",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[,]
                {
                    { 5m, 3L, 1L },
                    { 6m, 3L, 1L },
                    { 7m, 3L, 1L },
                    { 8m, 3L, 1L },
                    { 13m, 11L, 1L },
                    { 18m, 11L, 1L },
                    { 23m, 11L, 1L }
                });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[,]
                {
                    { 58m, 3L, 1L },
                    { 64m, 7L, 1L },
                    { 65m, 8L, 1L }
                });

            migrationBuilder.InsertData(
                table: "VendorStocks",
                columns: new[] { "Id", "CreatureTemplateId", "ItemTemplateId", "MaxStock", "PriceOverride", "RequiredQuestId", "RequiredQuestState", "RestockSeconds", "Sequence" },
                values: new object[,]
                {
                    { 1, 12m, 32m, null, null, null, null, null, 1L },
                    { 2, 12m, 33m, null, null, null, null, null, 2L },
                    { 3, 12m, 34m, null, null, null, null, null, 3L },
                    { 4, 12m, 35m, null, null, null, null, null, 4L },
                    { 5, 13m, 36m, null, null, null, null, null, 1L },
                    { 6, 13m, 37m, 2L, null, null, null, 1800L, 2L },
                    { 7, 13m, 38m, null, null, null, null, null, 3L },
                    { 8, 13m, 39m, null, null, null, null, null, 4L },
                    { 9, 13m, 40m, null, null, null, null, null, 5L },
                    { 10, 13m, 41m, null, null, null, null, null, 6L },
                    { 11, 13m, 42m, 2L, null, null, null, 1800L, 7L },
                    { 12, 13m, 43m, null, null, null, null, null, 8L },
                    { 13, 13m, 44m, null, null, null, null, null, 9L },
                    { 14, 13m, 45m, null, null, null, null, null, 10L },
                    { 15, 13m, 46m, null, null, null, null, null, 11L },
                    { 16, 13m, 47m, 2L, null, null, null, 1800L, 12L },
                    { 17, 13m, 48m, null, null, null, null, null, 13L },
                    { 18, 13m, 49m, null, null, null, null, null, 14L },
                    { 19, 13m, 50m, null, null, null, null, null, 15L }
                });

            migrationBuilder.InsertData(
                table: "LootTableEntries",
                columns: new[] { "LootTableId", "Sequence", "Chance", "GroupId", "ItemTemplateId", "MaxCount", "MinCount", "ReferenceTableId" },
                values: new object[,]
                {
                    { 11, 19, 2f, null, 30m, 1, 1, null },
                    { 11, 20, 2f, null, 31m, 1, 1, null }
                });

            migrationBuilder.InsertData(
                table: "QuestItemChoices",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 28m, 11L, 1L });

            migrationBuilder.InsertData(
                table: "VendorStocks",
                columns: new[] { "Id", "CreatureTemplateId", "ItemTemplateId", "MaxStock", "PriceOverride", "RequiredQuestId", "RequiredQuestState", "RestockSeconds", "Sequence" },
                values: new object[,]
                {
                    { 20, 13m, 51m, null, null, null, null, null, 16L },
                    { 21, 13m, 52m, 2L, null, null, null, 1800L, 17L },
                    { 22, 13m, 53m, null, null, null, null, null, 18L },
                    { 23, 13m, 54m, null, null, null, null, null, 19L },
                    { 24, 13m, 55m, null, null, null, null, null, 20L }
                });

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 66m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 67m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 71m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 72m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 74m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 75m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 79m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 80m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 81m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 82m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 83m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 84m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 85m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 86m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 87m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 88m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 89m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 90m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 91m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 92m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 93m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 94m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 95m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 96m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 97m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 98m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 99m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 100m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 101m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 102m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 103m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 68m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 69m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 70m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 73m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 76m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 77m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 78m);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DropColumn(
                name: "BaseKind",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "RequiresAnyAttribute",
                table: "ItemTemplates");
        }
    }
}
