using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RevalueAffixesForTheStartingZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemModifiers_Tier",
                table: "ItemModifiers");

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 4 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 5 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 9 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 10 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 14 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 15 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 19 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 20 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 24 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 25 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 29 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 30 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 34 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 35 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 39 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 40 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 44 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 45 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 49 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 50 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 54 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 55 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 59 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 60 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 64 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 65 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 69 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 70 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 74 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 75 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 79 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 80 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 84 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 85 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 89 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 90 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 94 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 95 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 134 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 135 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 139 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 140 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 184 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 185 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 189 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 193 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 194 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 203 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 204 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 208 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 209 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 218 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 4 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 4 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 4 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 4 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 5 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 5 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 5 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 5 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 9 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 9 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 9 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 9 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 10 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 10 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 10 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 10 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 14 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 14 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 14 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 14 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 15 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 15 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 15 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 15 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 19 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 19 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 19 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 19 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 20 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 20 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 20 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 20 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 24 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 24 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 24 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 24 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 25 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 25 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 25 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 25 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 29 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 29 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 29 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 29 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 30 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 30 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 30 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 30 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 34 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 34 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 34 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 35 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 35 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 35 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 39 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 39 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 39 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 39 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 40 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 40 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 40 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 40 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 44 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 44 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 44 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 44 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 45 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 45 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 45 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 45 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 49 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 49 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 49 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 49 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 50 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 50 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 50 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 50 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 54 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 54 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 54 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 54 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 55 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 55 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 55 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 55 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 56 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 56 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 57 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 57 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 58 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 58 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 59 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 59 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 59 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 59 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 59 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 60 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 60 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 60 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 60 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 60 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 61 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 61 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 62 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 62 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 63 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 63 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 64 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 64 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 64 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 64 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 64 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 65 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 65 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 65 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 65 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 65 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 66 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 66 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 67 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 67 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 68 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 68 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 69 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 69 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 69 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 69 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 69 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 70 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 70 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 70 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 70 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 70 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 71 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 71 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 72 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 72 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 73 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 73 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 74 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 74 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 74 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 74 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 74 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 75 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 75 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 75 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 75 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 75 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 76 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 76 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 77 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 77 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 78 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 78 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 79 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 79 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 79 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 79 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 79 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 80 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 80 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 80 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 80 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 80 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 81 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 81 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 82 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 82 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 83 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 83 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 84 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 84 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 84 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 85 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 85 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 85 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 86 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 86 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 87 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 87 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 88 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 88 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 89 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 89 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 89 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 90 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 90 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 90 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 91 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 91 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 92 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 92 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 93 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 93 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 94 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 94 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 94 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 95 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 95 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 95 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 96 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 97 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 98 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 99 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 100 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 104 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 105 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 109 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 110 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 114 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 115 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 119 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 120 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 124 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 125 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 129 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 130 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 134 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 134 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 134 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 135 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 135 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 135 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 139 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 139 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 139 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 139 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 140 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 140 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 140 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 140 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 141 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 142 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 143 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 144 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 145 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 146 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 147 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 148 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 149 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 150 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 151 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 152 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 153 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 154 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 155 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 156 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 157 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 158 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 159 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 160 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 164 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 165 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 169 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 170 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 174 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 175 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 179 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 180 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 184 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 184 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 184 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 184 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 185 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 185 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 185 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 185 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 186 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 187 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 188 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 189 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 189 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 189 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 189 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 190 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 190 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 190 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 191 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 191 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 191 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 192 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 192 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 192 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 193 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 193 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 193 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 193 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 193 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 194 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 194 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 194 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 194 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 194 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 195 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 195 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 195 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 196 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 196 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 196 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 197 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 197 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 197 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 198 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)26, 199 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 203 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 203 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 203 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 203 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 203 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)19, 204 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)20, 204 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)21, 204 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)22, 204 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 204 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 208 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 208 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 208 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)6, 209 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)12, 209 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)18, 209 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 210 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 211 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 212 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 213 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)25, 214 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)23, 218 });

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 75);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 79);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 84);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 85);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 89);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 90);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 94);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 95);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 99);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 109);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 110);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 114);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 115);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 119);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 120);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 124);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 125);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 129);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 130);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 134);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 135);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 139);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 140);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 144);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 145);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 149);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 150);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 154);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 155);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 159);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 160);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 164);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 165);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 169);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 170);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 174);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 175);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 179);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 180);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 184);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 185);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 189);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 193);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 194);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 198);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 199);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 203);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 204);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 208);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 209);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 213);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 214);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 218);

            migrationBuilder.InsertData(
                table: "ItemModifierGroups",
                columns: new[] { "Id", "Code", "Element", "Kind", "Name" },
                values: new object[] { 110, "S14", null, (byte)2, "of Deep Roots" });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 1 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 2 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 1, 2, 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 3 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 2, 3, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 6 },
                columns: new[] { "Max", "Max2", "Min2" },
                values: new object[] { 1, 1, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 7 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 1, 2, 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 8 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 2, 3, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 11 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 12 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 1, 2, 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 13 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 2, 3, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 16 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 17 },
                columns: new[] { "Max", "Max2", "Min2" },
                values: new object[] { 1, 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 18 },
                columns: new[] { "Max", "Max2", "Min2" },
                values: new object[] { 1, 4, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 21 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 22 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 1, 2, 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 23 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 2, 3, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 26 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 27 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 9 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 28 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 16, 13 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 31 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 32 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 33 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 36 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 37 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 38 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 41 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 42 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 43 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 46 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 47 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 48 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 51 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 52 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 53 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 56 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 57 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 58 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 61 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 62 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 63 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 66 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 67 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 68 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 71 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 72 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 73 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 76 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 77 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 78 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 81 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 82 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 83 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 86 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 87 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 88 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 91 },
                column: "Max2",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 92 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 1, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 93 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 1, 3, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 96 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 97 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 98 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 101 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 102 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 103 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 106 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 107 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 9 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 108 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 16, 13 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 111 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 112 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 9 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 113 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 20, 15 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 116 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 117 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 9 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 118 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 16, 13 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 121 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 122 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 123 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 126 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 127 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 9 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 128 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 16, 13 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 131 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 132 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 133 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 136 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 137 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 138 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 141 },
                column: "Max",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 142 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 143 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 146 },
                column: "Max",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 147 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 148 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 151 },
                column: "Max",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 152 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 153 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 156 },
                column: "Max",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 157 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 158 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 161 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 162 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 163 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 166 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 167 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 168 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 171 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 172 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 173 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 176 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 177 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 178 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 181 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 182 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 183 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 186 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 187 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 188 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 190 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 191 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 192 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 200 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 201 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 202 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 205 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 206 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 207 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 210 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 211 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 212 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 215 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 2, 1 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 216 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 217 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.InsertData(
                table: "ItemModifierWeights",
                columns: new[] { "BaseKind", "ModifierId", "Weight" },
                values: new object[,]
                {
                    { (byte)2, 36, 1000 },
                    { (byte)8, 36, 1000 },
                    { (byte)14, 36, 1000 },
                    { (byte)2, 37, 800 },
                    { (byte)8, 37, 800 },
                    { (byte)14, 37, 800 },
                    { (byte)2, 38, 600 },
                    { (byte)8, 38, 600 },
                    { (byte)14, 38, 600 },
                    { (byte)2, 41, 1000 },
                    { (byte)8, 41, 1000 },
                    { (byte)14, 41, 1000 },
                    { (byte)2, 42, 800 },
                    { (byte)8, 42, 800 },
                    { (byte)14, 42, 800 },
                    { (byte)2, 43, 600 },
                    { (byte)8, 43, 600 },
                    { (byte)14, 43, 600 },
                    { (byte)2, 46, 1000 },
                    { (byte)8, 46, 1000 },
                    { (byte)14, 46, 1000 },
                    { (byte)2, 47, 800 },
                    { (byte)8, 47, 800 },
                    { (byte)14, 47, 800 },
                    { (byte)2, 48, 600 },
                    { (byte)8, 48, 600 },
                    { (byte)14, 48, 600 },
                    { (byte)2, 51, 1000 },
                    { (byte)8, 51, 1000 },
                    { (byte)14, 51, 1000 },
                    { (byte)2, 52, 800 },
                    { (byte)8, 52, 800 },
                    { (byte)14, 52, 800 },
                    { (byte)2, 53, 600 },
                    { (byte)8, 53, 600 },
                    { (byte)14, 53, 600 },
                    { (byte)24, 161, 1000 },
                    { (byte)24, 162, 800 },
                    { (byte)24, 163, 600 },
                    { (byte)24, 166, 1000 },
                    { (byte)24, 167, 800 },
                    { (byte)24, 168, 600 },
                    { (byte)24, 171, 1000 },
                    { (byte)24, 172, 800 },
                    { (byte)24, 173, 600 },
                    { (byte)24, 176, 1000 },
                    { (byte)24, 177, 800 },
                    { (byte)24, 178, 600 },
                    { (byte)4, 190, 1000 },
                    { (byte)10, 190, 1000 },
                    { (byte)16, 190, 1000 },
                    { (byte)4, 191, 800 },
                    { (byte)10, 191, 800 },
                    { (byte)16, 191, 800 },
                    { (byte)4, 192, 600 },
                    { (byte)10, 192, 600 },
                    { (byte)16, 192, 600 },
                    { (byte)4, 195, 1000 },
                    { (byte)10, 195, 1000 },
                    { (byte)16, 195, 1000 },
                    { (byte)4, 196, 800 },
                    { (byte)10, 196, 800 },
                    { (byte)16, 196, 800 },
                    { (byte)4, 197, 600 },
                    { (byte)10, 197, 600 },
                    { (byte)16, 197, 600 },
                    { (byte)1, 200, 1000 },
                    { (byte)4, 200, 1000 },
                    { (byte)7, 200, 1000 },
                    { (byte)10, 200, 1000 },
                    { (byte)13, 200, 1000 },
                    { (byte)16, 200, 1000 },
                    { (byte)24, 200, 1000 },
                    { (byte)1, 201, 800 },
                    { (byte)4, 201, 800 },
                    { (byte)7, 201, 800 },
                    { (byte)10, 201, 800 },
                    { (byte)13, 201, 800 },
                    { (byte)16, 201, 800 },
                    { (byte)24, 201, 800 },
                    { (byte)1, 202, 600 },
                    { (byte)4, 202, 600 },
                    { (byte)7, 202, 600 },
                    { (byte)10, 202, 600 },
                    { (byte)13, 202, 600 },
                    { (byte)16, 202, 600 },
                    { (byte)24, 202, 600 }
                });

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 1,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 2,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 3,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 6,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 7,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 8,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 11,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 12,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 13,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 16,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 17,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 18,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 21,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 22,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 23,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 26,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 27,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 28,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 31,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 32,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 33,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 36,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 37,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 38,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 41,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 42,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 43,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 46,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 47,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 48,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 51,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 52,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 53,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 56,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 57,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 58,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 61,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 62,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 63,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 66,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 67,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 68,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 71,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 72,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 73,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 76,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 77,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 78,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 81,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 82,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 83,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 86,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 87,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 88,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 91,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 92,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 93,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 96,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 97,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 98,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 101,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 102,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 103,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 106,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 107,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 108,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 111,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 112,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 113,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 116,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 117,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 118,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 121,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 122,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 123,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 126,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 127,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 128,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 131,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 132,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 133,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 136,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 137,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 138,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 141,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 142,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 143,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 146,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 147,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 148,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 151,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 152,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 153,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 156,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 157,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 158,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 161,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 162,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 163,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 166,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 167,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 168,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 171,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 172,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 173,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 176,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 177,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 178,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 181,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 182,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 183,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 186,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 187,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 188,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 190,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 191,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 192,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 195,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 196,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 197,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 200,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 201,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 202,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 205,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 206,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 207,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 210,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 211,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 212,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 215,
                column: "Tier",
                value: (byte)9);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 216,
                column: "Tier",
                value: (byte)8);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 217,
                column: "Tier",
                value: (byte)7);

            migrationBuilder.InsertData(
                table: "ItemModifiers",
                columns: new[] { "Id", "GroupId", "RequiredItemLevel", "Tier" },
                values: new object[,]
                {
                    { 284, 110, (short)1, (byte)9 },
                    { 285, 110, (short)4, (byte)8 },
                    { 286, 110, (short)7, (byte)7 }
                });

            migrationBuilder.InsertData(
                table: "ItemModifierStats",
                columns: new[] { "Index", "ModifierId", "Max", "Max2", "Min", "Min2", "Stat" },
                values: new object[,]
                {
                    { (byte)0, 284, 2, null, 1, null, (short)53 },
                    { (byte)0, 285, 3, null, 2, null, (short)53 },
                    { (byte)0, 286, 4, null, 3, null, (short)53 }
                });

            migrationBuilder.InsertData(
                table: "ItemModifierWeights",
                columns: new[] { "BaseKind", "ModifierId", "Weight" },
                values: new object[,]
                {
                    { (byte)2, 284, 1000 },
                    { (byte)3, 284, 1000 },
                    { (byte)5, 284, 1000 },
                    { (byte)8, 284, 1000 },
                    { (byte)9, 284, 1000 },
                    { (byte)11, 284, 1000 },
                    { (byte)14, 284, 1000 },
                    { (byte)15, 284, 1000 },
                    { (byte)17, 284, 1000 },
                    { (byte)2, 285, 800 },
                    { (byte)3, 285, 800 },
                    { (byte)5, 285, 800 },
                    { (byte)8, 285, 800 },
                    { (byte)9, 285, 800 },
                    { (byte)11, 285, 800 },
                    { (byte)14, 285, 800 },
                    { (byte)15, 285, 800 },
                    { (byte)17, 285, 800 },
                    { (byte)2, 286, 600 },
                    { (byte)3, 286, 600 },
                    { (byte)5, 286, 600 },
                    { (byte)8, 286, 600 },
                    { (byte)9, 286, 600 },
                    { (byte)11, 286, 600 },
                    { (byte)14, 286, 600 },
                    { (byte)15, 286, 600 },
                    { (byte)17, 286, 600 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemModifiers_Tier",
                table: "ItemModifiers",
                sql: "\"Tier\" IS NULL OR \"Tier\" BETWEEN 1 AND 9");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemModifiers_Tier",
                table: "ItemModifiers");

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 36 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 36 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 36 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 37 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 37 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 37 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 38 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 38 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 38 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 41 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 41 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 41 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 42 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 42 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 42 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 43 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 43 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 43 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 46 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 46 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 46 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 47 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 47 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 47 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 48 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 48 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 48 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 51 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 51 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 51 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 52 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 52 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 52 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 53 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 53 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 53 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 161 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 162 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 163 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 166 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 167 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 168 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 171 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 172 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 173 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 176 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 177 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 178 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 190 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 190 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 190 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 191 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 191 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 191 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 192 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 192 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 192 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 195 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 195 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 195 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 196 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 196 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 196 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 197 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 197 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 197 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 200 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 201 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)1, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)4, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)7, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)10, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)13, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)16, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)24, 202 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 284 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 285 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)2, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)3, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)5, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)8, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)9, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)11, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)14, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)15, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifierWeights",
                keyColumns: new[] { "BaseKind", "ModifierId" },
                keyValues: new object[] { (byte)17, 286 });

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 284);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 285);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 286);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 110);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 1 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 2 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 3, 4, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 3 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 4, 6, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 6 },
                columns: new[] { "Max", "Max2", "Min2" },
                values: new object[] { 2, 3, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 7 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 3, 5, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 8 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 5, 7, 3, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 11 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 12 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 3, 4, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 13 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 4, 6, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 16 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 17 },
                columns: new[] { "Max", "Max2", "Min2" },
                values: new object[] { 4, 7, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 18 },
                columns: new[] { "Max", "Max2", "Min2" },
                values: new object[] { 5, 10, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 21 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 22 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 3, 4, 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 23 },
                columns: new[] { "Max", "Max2", "Min", "Min2" },
                values: new object[] { 4, 6, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 26 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 27 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 34, 20 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 28 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 49, 35 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 31 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 8 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 32 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 20, 13 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 33 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 30, 21 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 36 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 9, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 37 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 38 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 15 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 41 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 9, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 42 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 43 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 15 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 46 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 9, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 47 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 48 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 15 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 51 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 9, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 52 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 53 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 15 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 56 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 57 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 58 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 61 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 62 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 63 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 66 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 67 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 68 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 71 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 72 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 73 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 76 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 77 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 78 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 81 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 82 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 83 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 86 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 87 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 88 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 91 },
                column: "Max2",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 92 },
                columns: new[] { "Max", "Max2" },
                values: new object[] { 2, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 93 },
                columns: new[] { "Max", "Max2", "Min" },
                values: new object[] { 3, 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 96 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 9, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 97 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 98 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 29, 20 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 101 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 2 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 102 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 103 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 14, 8 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 106 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 107 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 34, 20 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 108 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 49, 35 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 111 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 112 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 39, 20 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 113 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 64, 40 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 116 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 117 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 34, 20 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 118 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 49, 35 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 121 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 122 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 123 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 17, 11 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 126 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 19, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 127 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 34, 20 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 128 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 49, 35 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 131 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 132 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 16, 11 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 133 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 22, 17 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 136 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 137 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 16, 11 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 138 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 22, 17 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 141 },
                column: "Max",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 142 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 143 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 146 },
                column: "Max",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 147 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 148 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 151 },
                column: "Max",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 152 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 153 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 156 },
                column: "Max",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 157 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 158 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 161 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 11, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 162 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 17, 12 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 163 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 23, 18 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 166 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 11, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 167 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 17, 12 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 168 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 23, 18 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 171 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 11, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 172 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 17, 12 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 173 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 23, 18 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 176 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 11, 6 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 177 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 17, 12 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 178 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 23, 18 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 181 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 7, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 182 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 10, 8 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 183 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 13, 11 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 186 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 187 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 188 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 190 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 4 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 191 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 9, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 192 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 10 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 200 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 12, 8 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 201 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 17, 13 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 202 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 22, 18 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 205 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 5, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 206 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 8 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 207 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 11, 11 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 210 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 211 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 212 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 7 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 215 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 4, 3 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 216 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 6, 5 });

            migrationBuilder.UpdateData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 217 },
                columns: new[] { "Max", "Min" },
                values: new object[] { 8, 7 });

            migrationBuilder.InsertData(
                table: "ItemModifierWeights",
                columns: new[] { "BaseKind", "ModifierId", "Weight" },
                values: new object[,]
                {
                    { (byte)25, 56, 1000 },
                    { (byte)26, 56, 1000 },
                    { (byte)25, 57, 800 },
                    { (byte)26, 57, 800 },
                    { (byte)25, 58, 600 },
                    { (byte)26, 58, 600 },
                    { (byte)25, 61, 1000 },
                    { (byte)26, 61, 1000 },
                    { (byte)25, 62, 800 },
                    { (byte)26, 62, 800 },
                    { (byte)25, 63, 600 },
                    { (byte)26, 63, 600 },
                    { (byte)25, 66, 1000 },
                    { (byte)26, 66, 1000 },
                    { (byte)25, 67, 800 },
                    { (byte)26, 67, 800 },
                    { (byte)25, 68, 600 },
                    { (byte)26, 68, 600 },
                    { (byte)25, 71, 1000 },
                    { (byte)26, 71, 1000 },
                    { (byte)25, 72, 800 },
                    { (byte)26, 72, 800 },
                    { (byte)25, 73, 600 },
                    { (byte)26, 73, 600 },
                    { (byte)25, 76, 1000 },
                    { (byte)26, 76, 1000 },
                    { (byte)25, 77, 800 },
                    { (byte)26, 77, 800 },
                    { (byte)25, 78, 600 },
                    { (byte)26, 78, 600 },
                    { (byte)24, 81, 1000 },
                    { (byte)25, 81, 1000 },
                    { (byte)24, 82, 800 },
                    { (byte)25, 82, 800 },
                    { (byte)24, 83, 600 },
                    { (byte)25, 83, 600 },
                    { (byte)24, 86, 1000 },
                    { (byte)25, 86, 1000 },
                    { (byte)24, 87, 800 },
                    { (byte)25, 87, 800 },
                    { (byte)24, 88, 600 },
                    { (byte)25, 88, 600 },
                    { (byte)24, 91, 1000 },
                    { (byte)25, 91, 1000 },
                    { (byte)24, 92, 800 },
                    { (byte)25, 92, 800 },
                    { (byte)24, 93, 600 },
                    { (byte)25, 93, 600 },
                    { (byte)2, 96, 1000 },
                    { (byte)4, 96, 1000 },
                    { (byte)8, 96, 1000 },
                    { (byte)10, 96, 1000 },
                    { (byte)14, 96, 1000 },
                    { (byte)16, 96, 1000 },
                    { (byte)26, 96, 1000 },
                    { (byte)2, 97, 800 },
                    { (byte)4, 97, 800 },
                    { (byte)8, 97, 800 },
                    { (byte)10, 97, 800 },
                    { (byte)14, 97, 800 },
                    { (byte)16, 97, 800 },
                    { (byte)26, 97, 800 },
                    { (byte)2, 98, 600 },
                    { (byte)4, 98, 600 },
                    { (byte)8, 98, 600 },
                    { (byte)10, 98, 600 },
                    { (byte)14, 98, 600 },
                    { (byte)16, 98, 600 },
                    { (byte)26, 98, 600 },
                    { (byte)1, 141, 1000 },
                    { (byte)2, 141, 1000 },
                    { (byte)4, 141, 1000 },
                    { (byte)5, 141, 1000 },
                    { (byte)6, 141, 1000 },
                    { (byte)7, 141, 1000 },
                    { (byte)8, 141, 1000 },
                    { (byte)10, 141, 1000 },
                    { (byte)11, 141, 1000 },
                    { (byte)12, 141, 1000 },
                    { (byte)13, 141, 1000 },
                    { (byte)14, 141, 1000 },
                    { (byte)16, 141, 1000 },
                    { (byte)17, 141, 1000 },
                    { (byte)18, 141, 1000 },
                    { (byte)19, 141, 1000 },
                    { (byte)20, 141, 1000 },
                    { (byte)22, 141, 1000 },
                    { (byte)23, 141, 1000 },
                    { (byte)25, 141, 1000 },
                    { (byte)1, 142, 800 },
                    { (byte)2, 142, 800 },
                    { (byte)4, 142, 800 },
                    { (byte)5, 142, 800 },
                    { (byte)6, 142, 800 },
                    { (byte)7, 142, 800 },
                    { (byte)8, 142, 800 },
                    { (byte)10, 142, 800 },
                    { (byte)11, 142, 800 },
                    { (byte)12, 142, 800 },
                    { (byte)13, 142, 800 },
                    { (byte)14, 142, 800 },
                    { (byte)16, 142, 800 },
                    { (byte)17, 142, 800 },
                    { (byte)18, 142, 800 },
                    { (byte)19, 142, 800 },
                    { (byte)20, 142, 800 },
                    { (byte)22, 142, 800 },
                    { (byte)23, 142, 800 },
                    { (byte)25, 142, 800 },
                    { (byte)1, 143, 600 },
                    { (byte)2, 143, 600 },
                    { (byte)4, 143, 600 },
                    { (byte)5, 143, 600 },
                    { (byte)6, 143, 600 },
                    { (byte)7, 143, 600 },
                    { (byte)8, 143, 600 },
                    { (byte)10, 143, 600 },
                    { (byte)11, 143, 600 },
                    { (byte)12, 143, 600 },
                    { (byte)13, 143, 600 },
                    { (byte)14, 143, 600 },
                    { (byte)16, 143, 600 },
                    { (byte)17, 143, 600 },
                    { (byte)18, 143, 600 },
                    { (byte)19, 143, 600 },
                    { (byte)20, 143, 600 },
                    { (byte)22, 143, 600 },
                    { (byte)23, 143, 600 },
                    { (byte)25, 143, 600 },
                    { (byte)1, 146, 1000 },
                    { (byte)2, 146, 1000 },
                    { (byte)3, 146, 1000 },
                    { (byte)4, 146, 1000 },
                    { (byte)6, 146, 1000 },
                    { (byte)7, 146, 1000 },
                    { (byte)8, 146, 1000 },
                    { (byte)9, 146, 1000 },
                    { (byte)10, 146, 1000 },
                    { (byte)12, 146, 1000 },
                    { (byte)13, 146, 1000 },
                    { (byte)14, 146, 1000 },
                    { (byte)15, 146, 1000 },
                    { (byte)16, 146, 1000 },
                    { (byte)18, 146, 1000 },
                    { (byte)19, 146, 1000 },
                    { (byte)21, 146, 1000 },
                    { (byte)22, 146, 1000 },
                    { (byte)25, 146, 1000 },
                    { (byte)1, 147, 800 },
                    { (byte)2, 147, 800 },
                    { (byte)3, 147, 800 },
                    { (byte)4, 147, 800 },
                    { (byte)6, 147, 800 },
                    { (byte)7, 147, 800 },
                    { (byte)8, 147, 800 },
                    { (byte)9, 147, 800 },
                    { (byte)10, 147, 800 },
                    { (byte)12, 147, 800 },
                    { (byte)13, 147, 800 },
                    { (byte)14, 147, 800 },
                    { (byte)15, 147, 800 },
                    { (byte)16, 147, 800 },
                    { (byte)18, 147, 800 },
                    { (byte)19, 147, 800 },
                    { (byte)21, 147, 800 },
                    { (byte)22, 147, 800 },
                    { (byte)25, 147, 800 },
                    { (byte)1, 148, 600 },
                    { (byte)2, 148, 600 },
                    { (byte)3, 148, 600 },
                    { (byte)4, 148, 600 },
                    { (byte)6, 148, 600 },
                    { (byte)7, 148, 600 },
                    { (byte)8, 148, 600 },
                    { (byte)9, 148, 600 },
                    { (byte)10, 148, 600 },
                    { (byte)12, 148, 600 },
                    { (byte)13, 148, 600 },
                    { (byte)14, 148, 600 },
                    { (byte)15, 148, 600 },
                    { (byte)16, 148, 600 },
                    { (byte)18, 148, 600 },
                    { (byte)19, 148, 600 },
                    { (byte)21, 148, 600 },
                    { (byte)22, 148, 600 },
                    { (byte)25, 148, 600 },
                    { (byte)1, 151, 1000 },
                    { (byte)3, 151, 1000 },
                    { (byte)4, 151, 1000 },
                    { (byte)5, 151, 1000 },
                    { (byte)6, 151, 1000 },
                    { (byte)7, 151, 1000 },
                    { (byte)9, 151, 1000 },
                    { (byte)10, 151, 1000 },
                    { (byte)11, 151, 1000 },
                    { (byte)12, 151, 1000 },
                    { (byte)13, 151, 1000 },
                    { (byte)15, 151, 1000 },
                    { (byte)16, 151, 1000 },
                    { (byte)17, 151, 1000 },
                    { (byte)18, 151, 1000 },
                    { (byte)19, 151, 1000 },
                    { (byte)20, 151, 1000 },
                    { (byte)21, 151, 1000 },
                    { (byte)24, 151, 1000 },
                    { (byte)25, 151, 1000 },
                    { (byte)1, 152, 800 },
                    { (byte)3, 152, 800 },
                    { (byte)4, 152, 800 },
                    { (byte)5, 152, 800 },
                    { (byte)6, 152, 800 },
                    { (byte)7, 152, 800 },
                    { (byte)9, 152, 800 },
                    { (byte)10, 152, 800 },
                    { (byte)11, 152, 800 },
                    { (byte)12, 152, 800 },
                    { (byte)13, 152, 800 },
                    { (byte)15, 152, 800 },
                    { (byte)16, 152, 800 },
                    { (byte)17, 152, 800 },
                    { (byte)18, 152, 800 },
                    { (byte)19, 152, 800 },
                    { (byte)20, 152, 800 },
                    { (byte)21, 152, 800 },
                    { (byte)24, 152, 800 },
                    { (byte)25, 152, 800 },
                    { (byte)1, 153, 600 },
                    { (byte)3, 153, 600 },
                    { (byte)4, 153, 600 },
                    { (byte)5, 153, 600 },
                    { (byte)6, 153, 600 },
                    { (byte)7, 153, 600 },
                    { (byte)9, 153, 600 },
                    { (byte)10, 153, 600 },
                    { (byte)11, 153, 600 },
                    { (byte)12, 153, 600 },
                    { (byte)13, 153, 600 },
                    { (byte)15, 153, 600 },
                    { (byte)16, 153, 600 },
                    { (byte)17, 153, 600 },
                    { (byte)18, 153, 600 },
                    { (byte)19, 153, 600 },
                    { (byte)20, 153, 600 },
                    { (byte)21, 153, 600 },
                    { (byte)24, 153, 600 },
                    { (byte)25, 153, 600 },
                    { (byte)1, 156, 1000 },
                    { (byte)2, 156, 1000 },
                    { (byte)3, 156, 1000 },
                    { (byte)4, 156, 1000 },
                    { (byte)5, 156, 1000 },
                    { (byte)7, 156, 1000 },
                    { (byte)8, 156, 1000 },
                    { (byte)9, 156, 1000 },
                    { (byte)10, 156, 1000 },
                    { (byte)11, 156, 1000 },
                    { (byte)13, 156, 1000 },
                    { (byte)14, 156, 1000 },
                    { (byte)15, 156, 1000 },
                    { (byte)16, 156, 1000 },
                    { (byte)17, 156, 1000 },
                    { (byte)23, 156, 1000 },
                    { (byte)25, 156, 1000 },
                    { (byte)26, 156, 1000 },
                    { (byte)1, 157, 800 },
                    { (byte)2, 157, 800 },
                    { (byte)3, 157, 800 },
                    { (byte)4, 157, 800 },
                    { (byte)5, 157, 800 },
                    { (byte)7, 157, 800 },
                    { (byte)8, 157, 800 },
                    { (byte)9, 157, 800 },
                    { (byte)10, 157, 800 },
                    { (byte)11, 157, 800 },
                    { (byte)13, 157, 800 },
                    { (byte)14, 157, 800 },
                    { (byte)15, 157, 800 },
                    { (byte)16, 157, 800 },
                    { (byte)17, 157, 800 },
                    { (byte)23, 157, 800 },
                    { (byte)25, 157, 800 },
                    { (byte)26, 157, 800 },
                    { (byte)1, 158, 600 },
                    { (byte)2, 158, 600 },
                    { (byte)3, 158, 600 },
                    { (byte)4, 158, 600 },
                    { (byte)5, 158, 600 },
                    { (byte)7, 158, 600 },
                    { (byte)8, 158, 600 },
                    { (byte)9, 158, 600 },
                    { (byte)10, 158, 600 },
                    { (byte)11, 158, 600 },
                    { (byte)13, 158, 600 },
                    { (byte)14, 158, 600 },
                    { (byte)15, 158, 600 },
                    { (byte)16, 158, 600 },
                    { (byte)17, 158, 600 },
                    { (byte)23, 158, 600 },
                    { (byte)25, 158, 600 },
                    { (byte)26, 158, 600 },
                    { (byte)2, 161, 1000 },
                    { (byte)3, 161, 1000 },
                    { (byte)4, 161, 1000 },
                    { (byte)5, 161, 1000 },
                    { (byte)6, 161, 1000 },
                    { (byte)8, 161, 1000 },
                    { (byte)9, 161, 1000 },
                    { (byte)10, 161, 1000 },
                    { (byte)11, 161, 1000 },
                    { (byte)12, 161, 1000 },
                    { (byte)14, 161, 1000 },
                    { (byte)15, 161, 1000 },
                    { (byte)16, 161, 1000 },
                    { (byte)17, 161, 1000 },
                    { (byte)18, 161, 1000 },
                    { (byte)26, 161, 1000 },
                    { (byte)2, 162, 800 },
                    { (byte)3, 162, 800 },
                    { (byte)4, 162, 800 },
                    { (byte)5, 162, 800 },
                    { (byte)6, 162, 800 },
                    { (byte)8, 162, 800 },
                    { (byte)9, 162, 800 },
                    { (byte)10, 162, 800 },
                    { (byte)11, 162, 800 },
                    { (byte)12, 162, 800 },
                    { (byte)14, 162, 800 },
                    { (byte)15, 162, 800 },
                    { (byte)16, 162, 800 },
                    { (byte)17, 162, 800 },
                    { (byte)18, 162, 800 },
                    { (byte)26, 162, 800 },
                    { (byte)2, 163, 600 },
                    { (byte)3, 163, 600 },
                    { (byte)4, 163, 600 },
                    { (byte)5, 163, 600 },
                    { (byte)6, 163, 600 },
                    { (byte)8, 163, 600 },
                    { (byte)9, 163, 600 },
                    { (byte)10, 163, 600 },
                    { (byte)11, 163, 600 },
                    { (byte)12, 163, 600 },
                    { (byte)14, 163, 600 },
                    { (byte)15, 163, 600 },
                    { (byte)16, 163, 600 },
                    { (byte)17, 163, 600 },
                    { (byte)18, 163, 600 },
                    { (byte)26, 163, 600 },
                    { (byte)1, 166, 1000 },
                    { (byte)2, 166, 1000 },
                    { (byte)3, 166, 1000 },
                    { (byte)4, 166, 1000 },
                    { (byte)5, 166, 1000 },
                    { (byte)7, 166, 1000 },
                    { (byte)8, 166, 1000 },
                    { (byte)9, 166, 1000 },
                    { (byte)10, 166, 1000 },
                    { (byte)11, 166, 1000 },
                    { (byte)13, 166, 1000 },
                    { (byte)14, 166, 1000 },
                    { (byte)15, 166, 1000 },
                    { (byte)16, 166, 1000 },
                    { (byte)17, 166, 1000 },
                    { (byte)26, 166, 1000 },
                    { (byte)1, 167, 800 },
                    { (byte)2, 167, 800 },
                    { (byte)3, 167, 800 },
                    { (byte)4, 167, 800 },
                    { (byte)5, 167, 800 },
                    { (byte)7, 167, 800 },
                    { (byte)8, 167, 800 },
                    { (byte)9, 167, 800 },
                    { (byte)10, 167, 800 },
                    { (byte)11, 167, 800 },
                    { (byte)13, 167, 800 },
                    { (byte)14, 167, 800 },
                    { (byte)15, 167, 800 },
                    { (byte)16, 167, 800 },
                    { (byte)17, 167, 800 },
                    { (byte)26, 167, 800 },
                    { (byte)1, 168, 600 },
                    { (byte)2, 168, 600 },
                    { (byte)3, 168, 600 },
                    { (byte)4, 168, 600 },
                    { (byte)5, 168, 600 },
                    { (byte)7, 168, 600 },
                    { (byte)8, 168, 600 },
                    { (byte)9, 168, 600 },
                    { (byte)10, 168, 600 },
                    { (byte)11, 168, 600 },
                    { (byte)13, 168, 600 },
                    { (byte)14, 168, 600 },
                    { (byte)15, 168, 600 },
                    { (byte)16, 168, 600 },
                    { (byte)17, 168, 600 },
                    { (byte)26, 168, 600 },
                    { (byte)1, 171, 1000 },
                    { (byte)2, 171, 1000 },
                    { (byte)3, 171, 1000 },
                    { (byte)4, 171, 1000 },
                    { (byte)6, 171, 1000 },
                    { (byte)7, 171, 1000 },
                    { (byte)8, 171, 1000 },
                    { (byte)9, 171, 1000 },
                    { (byte)10, 171, 1000 },
                    { (byte)12, 171, 1000 },
                    { (byte)13, 171, 1000 },
                    { (byte)14, 171, 1000 },
                    { (byte)15, 171, 1000 },
                    { (byte)16, 171, 1000 },
                    { (byte)18, 171, 1000 },
                    { (byte)26, 171, 1000 },
                    { (byte)1, 172, 800 },
                    { (byte)2, 172, 800 },
                    { (byte)3, 172, 800 },
                    { (byte)4, 172, 800 },
                    { (byte)6, 172, 800 },
                    { (byte)7, 172, 800 },
                    { (byte)8, 172, 800 },
                    { (byte)9, 172, 800 },
                    { (byte)10, 172, 800 },
                    { (byte)12, 172, 800 },
                    { (byte)13, 172, 800 },
                    { (byte)14, 172, 800 },
                    { (byte)15, 172, 800 },
                    { (byte)16, 172, 800 },
                    { (byte)18, 172, 800 },
                    { (byte)26, 172, 800 },
                    { (byte)1, 173, 600 },
                    { (byte)2, 173, 600 },
                    { (byte)3, 173, 600 },
                    { (byte)4, 173, 600 },
                    { (byte)6, 173, 600 },
                    { (byte)7, 173, 600 },
                    { (byte)8, 173, 600 },
                    { (byte)9, 173, 600 },
                    { (byte)10, 173, 600 },
                    { (byte)12, 173, 600 },
                    { (byte)13, 173, 600 },
                    { (byte)14, 173, 600 },
                    { (byte)15, 173, 600 },
                    { (byte)16, 173, 600 },
                    { (byte)18, 173, 600 },
                    { (byte)26, 173, 600 },
                    { (byte)1, 176, 1000 },
                    { (byte)2, 176, 1000 },
                    { (byte)4, 176, 1000 },
                    { (byte)5, 176, 1000 },
                    { (byte)6, 176, 1000 },
                    { (byte)7, 176, 1000 },
                    { (byte)8, 176, 1000 },
                    { (byte)10, 176, 1000 },
                    { (byte)11, 176, 1000 },
                    { (byte)12, 176, 1000 },
                    { (byte)13, 176, 1000 },
                    { (byte)14, 176, 1000 },
                    { (byte)16, 176, 1000 },
                    { (byte)17, 176, 1000 },
                    { (byte)18, 176, 1000 },
                    { (byte)26, 176, 1000 },
                    { (byte)1, 177, 800 },
                    { (byte)2, 177, 800 },
                    { (byte)4, 177, 800 },
                    { (byte)5, 177, 800 },
                    { (byte)6, 177, 800 },
                    { (byte)7, 177, 800 },
                    { (byte)8, 177, 800 },
                    { (byte)10, 177, 800 },
                    { (byte)11, 177, 800 },
                    { (byte)12, 177, 800 },
                    { (byte)13, 177, 800 },
                    { (byte)14, 177, 800 },
                    { (byte)16, 177, 800 },
                    { (byte)17, 177, 800 },
                    { (byte)18, 177, 800 },
                    { (byte)26, 177, 800 },
                    { (byte)1, 178, 600 },
                    { (byte)2, 178, 600 },
                    { (byte)4, 178, 600 },
                    { (byte)5, 178, 600 },
                    { (byte)6, 178, 600 },
                    { (byte)7, 178, 600 },
                    { (byte)8, 178, 600 },
                    { (byte)10, 178, 600 },
                    { (byte)11, 178, 600 },
                    { (byte)12, 178, 600 },
                    { (byte)13, 178, 600 },
                    { (byte)14, 178, 600 },
                    { (byte)16, 178, 600 },
                    { (byte)17, 178, 600 },
                    { (byte)18, 178, 600 },
                    { (byte)26, 178, 600 },
                    { (byte)26, 186, 1000 },
                    { (byte)26, 187, 800 },
                    { (byte)26, 188, 600 },
                    { (byte)20, 190, 1000 },
                    { (byte)25, 190, 1000 },
                    { (byte)26, 190, 1000 },
                    { (byte)20, 191, 800 },
                    { (byte)25, 191, 800 },
                    { (byte)26, 191, 800 },
                    { (byte)20, 192, 600 },
                    { (byte)25, 192, 600 },
                    { (byte)26, 192, 600 },
                    { (byte)24, 195, 1000 },
                    { (byte)25, 195, 1000 },
                    { (byte)26, 195, 1000 },
                    { (byte)24, 196, 800 },
                    { (byte)25, 196, 800 },
                    { (byte)26, 196, 800 },
                    { (byte)24, 197, 600 },
                    { (byte)25, 197, 600 },
                    { (byte)26, 197, 600 },
                    { (byte)25, 200, 1000 },
                    { (byte)25, 201, 800 },
                    { (byte)25, 202, 600 },
                    { (byte)25, 210, 1000 },
                    { (byte)25, 211, 800 },
                    { (byte)25, 212, 600 }
                });

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 1,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 2,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 3,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 6,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 7,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 8,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 11,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 12,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 13,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 16,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 17,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 18,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 21,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 22,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 23,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 26,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 27,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 28,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 31,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 32,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 33,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 36,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 37,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 38,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 41,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 42,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 43,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 46,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 47,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 48,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 51,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 52,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 53,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 56,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 57,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 58,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 61,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 62,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 63,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 66,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 67,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 68,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 71,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 72,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 73,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 76,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 77,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 78,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 81,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 82,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 83,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 86,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 87,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 88,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 91,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 92,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 93,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 96,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 97,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 98,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 101,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 102,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 103,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 106,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 107,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 108,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 111,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 112,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 113,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 116,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 117,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 118,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 121,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 122,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 123,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 126,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 127,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 128,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 131,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 132,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 133,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 136,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 137,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 138,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 141,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 142,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 143,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 146,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 147,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 148,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 151,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 152,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 153,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 156,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 157,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 158,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 161,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 162,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 163,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 166,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 167,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 168,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 171,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 172,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 173,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 176,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 177,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 178,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 181,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 182,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 183,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 186,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 187,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 188,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 190,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 191,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 192,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 195,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 196,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 197,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 200,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 201,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 202,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 205,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 206,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 207,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 210,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 211,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 212,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 215,
                column: "Tier",
                value: (byte)1);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 216,
                column: "Tier",
                value: (byte)2);

            migrationBuilder.UpdateData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 217,
                column: "Tier",
                value: (byte)3);

            migrationBuilder.InsertData(
                table: "ItemModifiers",
                columns: new[] { "Id", "GroupId", "RequiredItemLevel", "Tier" },
                values: new object[,]
                {
                    { 4, 1, (short)10, (byte)4 },
                    { 5, 1, (short)13, (byte)5 },
                    { 9, 2, (short)10, (byte)4 },
                    { 10, 2, (short)13, (byte)5 },
                    { 14, 3, (short)10, (byte)4 },
                    { 15, 3, (short)13, (byte)5 },
                    { 19, 4, (short)10, (byte)4 },
                    { 20, 4, (short)13, (byte)5 },
                    { 24, 5, (short)10, (byte)4 },
                    { 25, 5, (short)13, (byte)5 },
                    { 29, 6, (short)10, (byte)4 },
                    { 30, 6, (short)13, (byte)5 },
                    { 34, 7, (short)10, (byte)4 },
                    { 35, 7, (short)13, (byte)5 },
                    { 39, 8, (short)10, (byte)4 },
                    { 40, 8, (short)13, (byte)5 },
                    { 44, 9, (short)10, (byte)4 },
                    { 45, 9, (short)13, (byte)5 },
                    { 49, 10, (short)10, (byte)4 },
                    { 50, 10, (short)13, (byte)5 },
                    { 54, 11, (short)10, (byte)4 },
                    { 55, 11, (short)13, (byte)5 },
                    { 59, 12, (short)10, (byte)4 },
                    { 60, 12, (short)13, (byte)5 },
                    { 64, 13, (short)10, (byte)4 },
                    { 65, 13, (short)13, (byte)5 },
                    { 69, 14, (short)10, (byte)4 },
                    { 70, 14, (short)13, (byte)5 },
                    { 74, 15, (short)10, (byte)4 },
                    { 75, 15, (short)13, (byte)5 },
                    { 79, 16, (short)10, (byte)4 },
                    { 80, 16, (short)13, (byte)5 },
                    { 84, 17, (short)10, (byte)4 },
                    { 85, 17, (short)13, (byte)5 },
                    { 89, 18, (short)10, (byte)4 },
                    { 90, 18, (short)13, (byte)5 },
                    { 94, 19, (short)10, (byte)4 },
                    { 95, 19, (short)13, (byte)5 },
                    { 99, 20, (short)10, (byte)4 },
                    { 100, 20, (short)13, (byte)5 },
                    { 104, 21, (short)10, (byte)4 },
                    { 105, 21, (short)13, (byte)5 },
                    { 109, 22, (short)10, (byte)4 },
                    { 110, 22, (short)13, (byte)5 },
                    { 114, 23, (short)10, (byte)4 },
                    { 115, 23, (short)13, (byte)5 },
                    { 119, 24, (short)10, (byte)4 },
                    { 120, 24, (short)13, (byte)5 },
                    { 124, 25, (short)10, (byte)4 },
                    { 125, 25, (short)13, (byte)5 },
                    { 129, 26, (short)10, (byte)4 },
                    { 130, 26, (short)13, (byte)5 },
                    { 134, 27, (short)10, (byte)4 },
                    { 135, 27, (short)13, (byte)5 },
                    { 139, 28, (short)10, (byte)4 },
                    { 140, 28, (short)13, (byte)5 },
                    { 144, 29, (short)10, (byte)4 },
                    { 145, 29, (short)13, (byte)5 },
                    { 149, 30, (short)10, (byte)4 },
                    { 150, 30, (short)13, (byte)5 },
                    { 154, 31, (short)10, (byte)4 },
                    { 155, 31, (short)13, (byte)5 },
                    { 159, 32, (short)10, (byte)4 },
                    { 160, 32, (short)13, (byte)5 },
                    { 164, 33, (short)10, (byte)4 },
                    { 165, 33, (short)13, (byte)5 },
                    { 169, 34, (short)10, (byte)4 },
                    { 170, 34, (short)13, (byte)5 },
                    { 174, 35, (short)10, (byte)4 },
                    { 175, 35, (short)13, (byte)5 },
                    { 179, 36, (short)10, (byte)4 },
                    { 180, 36, (short)13, (byte)5 },
                    { 184, 37, (short)10, (byte)4 },
                    { 185, 37, (short)13, (byte)5 },
                    { 189, 38, (short)10, (byte)4 },
                    { 193, 39, (short)10, (byte)4 },
                    { 194, 39, (short)13, (byte)5 },
                    { 198, 40, (short)10, (byte)4 },
                    { 199, 40, (short)13, (byte)5 },
                    { 203, 41, (short)10, (byte)4 },
                    { 204, 41, (short)13, (byte)5 },
                    { 208, 42, (short)10, (byte)4 },
                    { 209, 42, (short)13, (byte)5 },
                    { 213, 43, (short)10, (byte)4 },
                    { 214, 43, (short)13, (byte)5 },
                    { 218, 44, (short)10, (byte)4 }
                });

            migrationBuilder.InsertData(
                table: "ItemModifierStats",
                columns: new[] { "Index", "ModifierId", "Max", "Max2", "Min", "Min2", "Stat" },
                values: new object[,]
                {
                    { (byte)0, 4, 6, 8, 5, 6, (short)35 },
                    { (byte)0, 5, 8, 11, 6, 8, (short)35 },
                    { (byte)0, 9, 7, 10, 4, 7, (short)36 },
                    { (byte)0, 10, 9, 13, 6, 9, (short)36 },
                    { (byte)0, 14, 6, 8, 4, 6, (short)37 },
                    { (byte)0, 15, 8, 11, 5, 8, (short)37 },
                    { (byte)0, 19, 8, 14, 2, 8, (short)38 },
                    { (byte)0, 20, 10, 18, 2, 10, (short)38 },
                    { (byte)0, 24, 6, 8, 4, 6, (short)39 },
                    { (byte)0, 25, 8, 11, 5, 8, (short)39 },
                    { (byte)0, 29, 64, null, 50, null, (short)40 },
                    { (byte)0, 30, 79, null, 65, null, (short)40 },
                    { (byte)0, 34, 40, null, 31, null, (short)26 },
                    { (byte)0, 35, 50, null, 41, null, (short)26 },
                    { (byte)0, 39, 24, null, 20, null, (short)21 },
                    { (byte)0, 40, 29, null, 25, null, (short)21 },
                    { (byte)0, 44, 24, null, 20, null, (short)22 },
                    { (byte)0, 45, 29, null, 25, null, (short)22 },
                    { (byte)0, 49, 24, null, 20, null, (short)23 },
                    { (byte)0, 50, 29, null, 25, null, (short)23 },
                    { (byte)0, 54, 24, null, 20, null, (short)24 },
                    { (byte)0, 55, 29, null, 25, null, (short)24 },
                    { (byte)0, 59, 3, 5, 2, 3, (short)45 },
                    { (byte)0, 60, 4, 6, 3, 4, (short)45 },
                    { (byte)0, 64, 3, 5, 2, 3, (short)46 },
                    { (byte)0, 65, 4, 6, 3, 4, (short)46 },
                    { (byte)0, 69, 3, 5, 2, 3, (short)47 },
                    { (byte)0, 70, 4, 6, 3, 4, (short)47 },
                    { (byte)0, 74, 3, 5, 2, 3, (short)48 },
                    { (byte)0, 75, 4, 6, 3, 4, (short)48 },
                    { (byte)0, 79, 3, 5, 2, 3, (short)49 },
                    { (byte)0, 80, 4, 6, 3, 4, (short)49 },
                    { (byte)0, 84, 3, 5, 2, 3, (short)50 },
                    { (byte)0, 85, 4, 6, 3, 4, (short)50 },
                    { (byte)0, 89, 3, 5, 2, 3, (short)51 },
                    { (byte)0, 90, 4, 6, 3, 4, (short)51 },
                    { (byte)0, 94, 3, 5, 2, 3, (short)52 },
                    { (byte)0, 95, 4, 6, 3, 4, (short)52 },
                    { (byte)0, 99, 44, null, 30, null, (short)10 },
                    { (byte)0, 100, 59, null, 45, null, (short)10 },
                    { (byte)0, 104, 22, null, 14, null, (short)4 },
                    { (byte)0, 105, 32, null, 22, null, (short)4 },
                    { (byte)0, 109, 64, null, 50, null, (short)41 },
                    { (byte)0, 110, 79, null, 65, null, (short)41 },
                    { (byte)0, 114, 94, null, 65, null, (short)6 },
                    { (byte)0, 115, 129, null, 95, null, (short)6 },
                    { (byte)0, 119, 64, null, 50, null, (short)42 },
                    { (byte)0, 120, 79, null, 65, null, (short)42 },
                    { (byte)0, 124, 26, null, 18, null, (short)14 },
                    { (byte)0, 125, 37, null, 27, null, (short)14 },
                    { (byte)0, 129, 64, null, 50, null, (short)43 },
                    { (byte)0, 130, 79, null, 65, null, (short)43 },
                    { (byte)0, 134, 28, null, 23, null, (short)30 },
                    { (byte)0, 135, 34, null, 29, null, (short)30 },
                    { (byte)0, 139, 28, null, 23, null, (short)31 },
                    { (byte)0, 140, 34, null, 29, null, (short)31 },
                    { (byte)0, 144, 15, null, 11, null, (short)1 },
                    { (byte)0, 145, 20, null, 16, null, (short)1 },
                    { (byte)0, 149, 15, null, 11, null, (short)2 },
                    { (byte)0, 150, 20, null, 16, null, (short)2 },
                    { (byte)0, 154, 15, null, 11, null, (short)3 },
                    { (byte)0, 155, 20, null, 16, null, (short)3 },
                    { (byte)0, 159, 15, null, 11, null, (short)0 },
                    { (byte)0, 160, 20, null, 16, null, (short)0 },
                    { (byte)0, 164, 29, null, 24, null, (short)15 },
                    { (byte)0, 165, 35, null, 30, null, (short)15 },
                    { (byte)0, 169, 29, null, 24, null, (short)16 },
                    { (byte)0, 170, 35, null, 30, null, (short)16 },
                    { (byte)0, 174, 29, null, 24, null, (short)17 },
                    { (byte)0, 175, 35, null, 30, null, (short)17 },
                    { (byte)0, 179, 29, null, 24, null, (short)18 },
                    { (byte)0, 180, 35, null, 30, null, (short)18 },
                    { (byte)0, 184, 16, null, 14, null, (short)44 },
                    { (byte)0, 185, 19, null, 17, null, (short)44 },
                    { (byte)0, 189, 10, null, 9, null, (short)12 },
                    { (byte)0, 193, 15, null, 13, null, (short)33 },
                    { (byte)0, 194, 18, null, 16, null, (short)33 },
                    { (byte)0, 198, 4, null, 4, null, (short)7 },
                    { (byte)0, 199, 5, null, 5, null, (short)7 },
                    { (byte)0, 203, 27, null, 23, null, (short)32 },
                    { (byte)0, 204, 32, null, 28, null, (short)32 },
                    { (byte)0, 208, 14, null, 14, null, (short)13 },
                    { (byte)0, 209, 17, null, 17, null, (short)13 },
                    { (byte)0, 213, 10, null, 9, null, (short)34 },
                    { (byte)0, 214, 12, null, 11, null, (short)34 },
                    { (byte)0, 218, 10, null, 9, null, (short)5 }
                });

            migrationBuilder.InsertData(
                table: "ItemModifierWeights",
                columns: new[] { "BaseKind", "ModifierId", "Weight" },
                values: new object[,]
                {
                    { (byte)19, 4, 400 },
                    { (byte)20, 4, 400 },
                    { (byte)21, 4, 400 },
                    { (byte)22, 4, 400 },
                    { (byte)19, 5, 200 },
                    { (byte)20, 5, 200 },
                    { (byte)21, 5, 200 },
                    { (byte)22, 5, 200 },
                    { (byte)19, 9, 400 },
                    { (byte)20, 9, 400 },
                    { (byte)21, 9, 400 },
                    { (byte)22, 9, 400 },
                    { (byte)19, 10, 200 },
                    { (byte)20, 10, 200 },
                    { (byte)21, 10, 200 },
                    { (byte)22, 10, 200 },
                    { (byte)19, 14, 400 },
                    { (byte)20, 14, 400 },
                    { (byte)21, 14, 400 },
                    { (byte)22, 14, 400 },
                    { (byte)19, 15, 200 },
                    { (byte)20, 15, 200 },
                    { (byte)21, 15, 200 },
                    { (byte)22, 15, 200 },
                    { (byte)19, 19, 400 },
                    { (byte)20, 19, 400 },
                    { (byte)21, 19, 400 },
                    { (byte)22, 19, 400 },
                    { (byte)19, 20, 200 },
                    { (byte)20, 20, 200 },
                    { (byte)21, 20, 200 },
                    { (byte)22, 20, 200 },
                    { (byte)19, 24, 400 },
                    { (byte)20, 24, 400 },
                    { (byte)21, 24, 400 },
                    { (byte)22, 24, 400 },
                    { (byte)19, 25, 200 },
                    { (byte)20, 25, 200 },
                    { (byte)21, 25, 200 },
                    { (byte)22, 25, 200 },
                    { (byte)19, 29, 400 },
                    { (byte)20, 29, 400 },
                    { (byte)21, 29, 400 },
                    { (byte)22, 29, 400 },
                    { (byte)19, 30, 200 },
                    { (byte)20, 30, 200 },
                    { (byte)21, 30, 200 },
                    { (byte)22, 30, 200 },
                    { (byte)20, 34, 400 },
                    { (byte)21, 34, 400 },
                    { (byte)24, 34, 400 },
                    { (byte)20, 35, 200 },
                    { (byte)21, 35, 200 },
                    { (byte)24, 35, 200 },
                    { (byte)21, 39, 400 },
                    { (byte)24, 39, 400 },
                    { (byte)25, 39, 400 },
                    { (byte)26, 39, 400 },
                    { (byte)21, 40, 200 },
                    { (byte)24, 40, 200 },
                    { (byte)25, 40, 200 },
                    { (byte)26, 40, 200 },
                    { (byte)21, 44, 400 },
                    { (byte)24, 44, 400 },
                    { (byte)25, 44, 400 },
                    { (byte)26, 44, 400 },
                    { (byte)21, 45, 200 },
                    { (byte)24, 45, 200 },
                    { (byte)25, 45, 200 },
                    { (byte)26, 45, 200 },
                    { (byte)21, 49, 400 },
                    { (byte)24, 49, 400 },
                    { (byte)25, 49, 400 },
                    { (byte)26, 49, 400 },
                    { (byte)21, 50, 200 },
                    { (byte)24, 50, 200 },
                    { (byte)25, 50, 200 },
                    { (byte)26, 50, 200 },
                    { (byte)21, 54, 400 },
                    { (byte)24, 54, 400 },
                    { (byte)25, 54, 400 },
                    { (byte)26, 54, 400 },
                    { (byte)21, 55, 200 },
                    { (byte)24, 55, 200 },
                    { (byte)25, 55, 200 },
                    { (byte)26, 55, 200 },
                    { (byte)4, 59, 400 },
                    { (byte)10, 59, 400 },
                    { (byte)16, 59, 400 },
                    { (byte)25, 59, 400 },
                    { (byte)26, 59, 400 },
                    { (byte)4, 60, 200 },
                    { (byte)10, 60, 200 },
                    { (byte)16, 60, 200 },
                    { (byte)25, 60, 200 },
                    { (byte)26, 60, 200 },
                    { (byte)4, 64, 400 },
                    { (byte)10, 64, 400 },
                    { (byte)16, 64, 400 },
                    { (byte)25, 64, 400 },
                    { (byte)26, 64, 400 },
                    { (byte)4, 65, 200 },
                    { (byte)10, 65, 200 },
                    { (byte)16, 65, 200 },
                    { (byte)25, 65, 200 },
                    { (byte)26, 65, 200 },
                    { (byte)4, 69, 400 },
                    { (byte)10, 69, 400 },
                    { (byte)16, 69, 400 },
                    { (byte)25, 69, 400 },
                    { (byte)26, 69, 400 },
                    { (byte)4, 70, 200 },
                    { (byte)10, 70, 200 },
                    { (byte)16, 70, 200 },
                    { (byte)25, 70, 200 },
                    { (byte)26, 70, 200 },
                    { (byte)4, 74, 400 },
                    { (byte)10, 74, 400 },
                    { (byte)16, 74, 400 },
                    { (byte)25, 74, 400 },
                    { (byte)26, 74, 400 },
                    { (byte)4, 75, 200 },
                    { (byte)10, 75, 200 },
                    { (byte)16, 75, 200 },
                    { (byte)25, 75, 200 },
                    { (byte)26, 75, 200 },
                    { (byte)4, 79, 400 },
                    { (byte)10, 79, 400 },
                    { (byte)16, 79, 400 },
                    { (byte)25, 79, 400 },
                    { (byte)26, 79, 400 },
                    { (byte)4, 80, 200 },
                    { (byte)10, 80, 200 },
                    { (byte)16, 80, 200 },
                    { (byte)25, 80, 200 },
                    { (byte)26, 80, 200 },
                    { (byte)24, 84, 400 },
                    { (byte)25, 84, 400 },
                    { (byte)26, 84, 400 },
                    { (byte)24, 85, 200 },
                    { (byte)25, 85, 200 },
                    { (byte)26, 85, 200 },
                    { (byte)24, 89, 400 },
                    { (byte)25, 89, 400 },
                    { (byte)26, 89, 400 },
                    { (byte)24, 90, 200 },
                    { (byte)25, 90, 200 },
                    { (byte)26, 90, 200 },
                    { (byte)24, 94, 400 },
                    { (byte)25, 94, 400 },
                    { (byte)26, 94, 400 },
                    { (byte)24, 95, 200 },
                    { (byte)25, 95, 200 },
                    { (byte)26, 95, 200 },
                    { (byte)1, 99, 400 },
                    { (byte)2, 99, 400 },
                    { (byte)3, 99, 400 },
                    { (byte)4, 99, 400 },
                    { (byte)5, 99, 400 },
                    { (byte)6, 99, 400 },
                    { (byte)7, 99, 400 },
                    { (byte)8, 99, 400 },
                    { (byte)9, 99, 400 },
                    { (byte)10, 99, 400 },
                    { (byte)11, 99, 400 },
                    { (byte)12, 99, 400 },
                    { (byte)13, 99, 400 },
                    { (byte)14, 99, 400 },
                    { (byte)15, 99, 400 },
                    { (byte)16, 99, 400 },
                    { (byte)17, 99, 400 },
                    { (byte)18, 99, 400 },
                    { (byte)23, 99, 400 },
                    { (byte)25, 99, 400 },
                    { (byte)26, 99, 400 },
                    { (byte)1, 100, 200 },
                    { (byte)2, 100, 200 },
                    { (byte)3, 100, 200 },
                    { (byte)4, 100, 200 },
                    { (byte)5, 100, 200 },
                    { (byte)6, 100, 200 },
                    { (byte)7, 100, 200 },
                    { (byte)8, 100, 200 },
                    { (byte)9, 100, 200 },
                    { (byte)10, 100, 200 },
                    { (byte)11, 100, 200 },
                    { (byte)12, 100, 200 },
                    { (byte)13, 100, 200 },
                    { (byte)14, 100, 200 },
                    { (byte)15, 100, 200 },
                    { (byte)16, 100, 200 },
                    { (byte)17, 100, 200 },
                    { (byte)18, 100, 200 },
                    { (byte)23, 100, 200 },
                    { (byte)25, 100, 200 },
                    { (byte)26, 100, 200 },
                    { (byte)1, 104, 400 },
                    { (byte)2, 104, 400 },
                    { (byte)3, 104, 400 },
                    { (byte)4, 104, 400 },
                    { (byte)5, 104, 400 },
                    { (byte)6, 104, 400 },
                    { (byte)23, 104, 400 },
                    { (byte)1, 105, 200 },
                    { (byte)2, 105, 200 },
                    { (byte)3, 105, 200 },
                    { (byte)4, 105, 200 },
                    { (byte)5, 105, 200 },
                    { (byte)6, 105, 200 },
                    { (byte)23, 105, 200 },
                    { (byte)1, 109, 400 },
                    { (byte)2, 109, 400 },
                    { (byte)3, 109, 400 },
                    { (byte)4, 109, 400 },
                    { (byte)5, 109, 400 },
                    { (byte)6, 109, 400 },
                    { (byte)23, 109, 400 },
                    { (byte)1, 110, 200 },
                    { (byte)2, 110, 200 },
                    { (byte)3, 110, 200 },
                    { (byte)4, 110, 200 },
                    { (byte)5, 110, 200 },
                    { (byte)6, 110, 200 },
                    { (byte)23, 110, 200 },
                    { (byte)7, 114, 400 },
                    { (byte)8, 114, 400 },
                    { (byte)9, 114, 400 },
                    { (byte)10, 114, 400 },
                    { (byte)11, 114, 400 },
                    { (byte)12, 114, 400 },
                    { (byte)7, 115, 200 },
                    { (byte)8, 115, 200 },
                    { (byte)9, 115, 200 },
                    { (byte)10, 115, 200 },
                    { (byte)11, 115, 200 },
                    { (byte)12, 115, 200 },
                    { (byte)7, 119, 400 },
                    { (byte)8, 119, 400 },
                    { (byte)9, 119, 400 },
                    { (byte)10, 119, 400 },
                    { (byte)11, 119, 400 },
                    { (byte)12, 119, 400 },
                    { (byte)7, 120, 200 },
                    { (byte)8, 120, 200 },
                    { (byte)9, 120, 200 },
                    { (byte)10, 120, 200 },
                    { (byte)11, 120, 200 },
                    { (byte)12, 120, 200 },
                    { (byte)13, 124, 400 },
                    { (byte)14, 124, 400 },
                    { (byte)15, 124, 400 },
                    { (byte)16, 124, 400 },
                    { (byte)17, 124, 400 },
                    { (byte)18, 124, 400 },
                    { (byte)24, 124, 400 },
                    { (byte)13, 125, 200 },
                    { (byte)14, 125, 200 },
                    { (byte)15, 125, 200 },
                    { (byte)16, 125, 200 },
                    { (byte)17, 125, 200 },
                    { (byte)18, 125, 200 },
                    { (byte)24, 125, 200 },
                    { (byte)13, 129, 400 },
                    { (byte)14, 129, 400 },
                    { (byte)15, 129, 400 },
                    { (byte)16, 129, 400 },
                    { (byte)17, 129, 400 },
                    { (byte)18, 129, 400 },
                    { (byte)24, 129, 400 },
                    { (byte)13, 130, 200 },
                    { (byte)14, 130, 200 },
                    { (byte)15, 130, 200 },
                    { (byte)16, 130, 200 },
                    { (byte)17, 130, 200 },
                    { (byte)18, 130, 200 },
                    { (byte)24, 130, 200 },
                    { (byte)21, 134, 400 },
                    { (byte)24, 134, 400 },
                    { (byte)25, 134, 400 },
                    { (byte)21, 135, 200 },
                    { (byte)24, 135, 200 },
                    { (byte)25, 135, 200 },
                    { (byte)20, 139, 400 },
                    { (byte)21, 139, 400 },
                    { (byte)24, 139, 400 },
                    { (byte)25, 139, 400 },
                    { (byte)20, 140, 200 },
                    { (byte)21, 140, 200 },
                    { (byte)24, 140, 200 },
                    { (byte)25, 140, 200 },
                    { (byte)1, 144, 400 },
                    { (byte)2, 144, 400 },
                    { (byte)3, 144, 400 },
                    { (byte)4, 144, 400 },
                    { (byte)5, 144, 400 },
                    { (byte)6, 144, 400 },
                    { (byte)7, 144, 400 },
                    { (byte)8, 144, 400 },
                    { (byte)9, 144, 400 },
                    { (byte)10, 144, 400 },
                    { (byte)11, 144, 400 },
                    { (byte)12, 144, 400 },
                    { (byte)13, 144, 400 },
                    { (byte)14, 144, 400 },
                    { (byte)15, 144, 400 },
                    { (byte)16, 144, 400 },
                    { (byte)17, 144, 400 },
                    { (byte)18, 144, 400 },
                    { (byte)19, 144, 400 },
                    { (byte)20, 144, 400 },
                    { (byte)22, 144, 400 },
                    { (byte)23, 144, 400 },
                    { (byte)25, 144, 400 },
                    { (byte)26, 144, 400 },
                    { (byte)1, 145, 200 },
                    { (byte)2, 145, 200 },
                    { (byte)3, 145, 200 },
                    { (byte)4, 145, 200 },
                    { (byte)5, 145, 200 },
                    { (byte)6, 145, 200 },
                    { (byte)7, 145, 200 },
                    { (byte)8, 145, 200 },
                    { (byte)9, 145, 200 },
                    { (byte)10, 145, 200 },
                    { (byte)11, 145, 200 },
                    { (byte)12, 145, 200 },
                    { (byte)13, 145, 200 },
                    { (byte)14, 145, 200 },
                    { (byte)15, 145, 200 },
                    { (byte)16, 145, 200 },
                    { (byte)17, 145, 200 },
                    { (byte)18, 145, 200 },
                    { (byte)19, 145, 200 },
                    { (byte)20, 145, 200 },
                    { (byte)22, 145, 200 },
                    { (byte)23, 145, 200 },
                    { (byte)25, 145, 200 },
                    { (byte)26, 145, 200 },
                    { (byte)1, 149, 400 },
                    { (byte)2, 149, 400 },
                    { (byte)3, 149, 400 },
                    { (byte)4, 149, 400 },
                    { (byte)5, 149, 400 },
                    { (byte)6, 149, 400 },
                    { (byte)7, 149, 400 },
                    { (byte)8, 149, 400 },
                    { (byte)9, 149, 400 },
                    { (byte)10, 149, 400 },
                    { (byte)11, 149, 400 },
                    { (byte)12, 149, 400 },
                    { (byte)13, 149, 400 },
                    { (byte)14, 149, 400 },
                    { (byte)15, 149, 400 },
                    { (byte)16, 149, 400 },
                    { (byte)17, 149, 400 },
                    { (byte)18, 149, 400 },
                    { (byte)19, 149, 400 },
                    { (byte)21, 149, 400 },
                    { (byte)22, 149, 400 },
                    { (byte)25, 149, 400 },
                    { (byte)26, 149, 400 },
                    { (byte)1, 150, 200 },
                    { (byte)2, 150, 200 },
                    { (byte)3, 150, 200 },
                    { (byte)4, 150, 200 },
                    { (byte)5, 150, 200 },
                    { (byte)6, 150, 200 },
                    { (byte)7, 150, 200 },
                    { (byte)8, 150, 200 },
                    { (byte)9, 150, 200 },
                    { (byte)10, 150, 200 },
                    { (byte)11, 150, 200 },
                    { (byte)12, 150, 200 },
                    { (byte)13, 150, 200 },
                    { (byte)14, 150, 200 },
                    { (byte)15, 150, 200 },
                    { (byte)16, 150, 200 },
                    { (byte)17, 150, 200 },
                    { (byte)18, 150, 200 },
                    { (byte)19, 150, 200 },
                    { (byte)21, 150, 200 },
                    { (byte)22, 150, 200 },
                    { (byte)25, 150, 200 },
                    { (byte)26, 150, 200 },
                    { (byte)1, 154, 400 },
                    { (byte)2, 154, 400 },
                    { (byte)3, 154, 400 },
                    { (byte)4, 154, 400 },
                    { (byte)5, 154, 400 },
                    { (byte)6, 154, 400 },
                    { (byte)7, 154, 400 },
                    { (byte)8, 154, 400 },
                    { (byte)9, 154, 400 },
                    { (byte)10, 154, 400 },
                    { (byte)11, 154, 400 },
                    { (byte)12, 154, 400 },
                    { (byte)13, 154, 400 },
                    { (byte)14, 154, 400 },
                    { (byte)15, 154, 400 },
                    { (byte)16, 154, 400 },
                    { (byte)17, 154, 400 },
                    { (byte)18, 154, 400 },
                    { (byte)19, 154, 400 },
                    { (byte)20, 154, 400 },
                    { (byte)21, 154, 400 },
                    { (byte)24, 154, 400 },
                    { (byte)25, 154, 400 },
                    { (byte)26, 154, 400 },
                    { (byte)1, 155, 200 },
                    { (byte)2, 155, 200 },
                    { (byte)3, 155, 200 },
                    { (byte)4, 155, 200 },
                    { (byte)5, 155, 200 },
                    { (byte)6, 155, 200 },
                    { (byte)7, 155, 200 },
                    { (byte)8, 155, 200 },
                    { (byte)9, 155, 200 },
                    { (byte)10, 155, 200 },
                    { (byte)11, 155, 200 },
                    { (byte)12, 155, 200 },
                    { (byte)13, 155, 200 },
                    { (byte)14, 155, 200 },
                    { (byte)15, 155, 200 },
                    { (byte)16, 155, 200 },
                    { (byte)17, 155, 200 },
                    { (byte)18, 155, 200 },
                    { (byte)19, 155, 200 },
                    { (byte)20, 155, 200 },
                    { (byte)21, 155, 200 },
                    { (byte)24, 155, 200 },
                    { (byte)25, 155, 200 },
                    { (byte)26, 155, 200 },
                    { (byte)1, 159, 400 },
                    { (byte)2, 159, 400 },
                    { (byte)3, 159, 400 },
                    { (byte)4, 159, 400 },
                    { (byte)5, 159, 400 },
                    { (byte)6, 159, 400 },
                    { (byte)7, 159, 400 },
                    { (byte)8, 159, 400 },
                    { (byte)9, 159, 400 },
                    { (byte)10, 159, 400 },
                    { (byte)11, 159, 400 },
                    { (byte)12, 159, 400 },
                    { (byte)13, 159, 400 },
                    { (byte)14, 159, 400 },
                    { (byte)15, 159, 400 },
                    { (byte)16, 159, 400 },
                    { (byte)17, 159, 400 },
                    { (byte)18, 159, 400 },
                    { (byte)23, 159, 400 },
                    { (byte)25, 159, 400 },
                    { (byte)26, 159, 400 },
                    { (byte)1, 160, 200 },
                    { (byte)2, 160, 200 },
                    { (byte)3, 160, 200 },
                    { (byte)4, 160, 200 },
                    { (byte)5, 160, 200 },
                    { (byte)6, 160, 200 },
                    { (byte)7, 160, 200 },
                    { (byte)8, 160, 200 },
                    { (byte)9, 160, 200 },
                    { (byte)10, 160, 200 },
                    { (byte)11, 160, 200 },
                    { (byte)12, 160, 200 },
                    { (byte)13, 160, 200 },
                    { (byte)14, 160, 200 },
                    { (byte)15, 160, 200 },
                    { (byte)16, 160, 200 },
                    { (byte)17, 160, 200 },
                    { (byte)18, 160, 200 },
                    { (byte)23, 160, 200 },
                    { (byte)25, 160, 200 },
                    { (byte)26, 160, 200 },
                    { (byte)1, 164, 400 },
                    { (byte)2, 164, 400 },
                    { (byte)3, 164, 400 },
                    { (byte)4, 164, 400 },
                    { (byte)5, 164, 400 },
                    { (byte)6, 164, 400 },
                    { (byte)7, 164, 400 },
                    { (byte)8, 164, 400 },
                    { (byte)9, 164, 400 },
                    { (byte)10, 164, 400 },
                    { (byte)11, 164, 400 },
                    { (byte)12, 164, 400 },
                    { (byte)13, 164, 400 },
                    { (byte)14, 164, 400 },
                    { (byte)15, 164, 400 },
                    { (byte)16, 164, 400 },
                    { (byte)17, 164, 400 },
                    { (byte)18, 164, 400 },
                    { (byte)23, 164, 400 },
                    { (byte)25, 164, 400 },
                    { (byte)26, 164, 400 },
                    { (byte)1, 165, 200 },
                    { (byte)2, 165, 200 },
                    { (byte)3, 165, 200 },
                    { (byte)4, 165, 200 },
                    { (byte)5, 165, 200 },
                    { (byte)6, 165, 200 },
                    { (byte)7, 165, 200 },
                    { (byte)8, 165, 200 },
                    { (byte)9, 165, 200 },
                    { (byte)10, 165, 200 },
                    { (byte)11, 165, 200 },
                    { (byte)12, 165, 200 },
                    { (byte)13, 165, 200 },
                    { (byte)14, 165, 200 },
                    { (byte)15, 165, 200 },
                    { (byte)16, 165, 200 },
                    { (byte)17, 165, 200 },
                    { (byte)18, 165, 200 },
                    { (byte)23, 165, 200 },
                    { (byte)25, 165, 200 },
                    { (byte)26, 165, 200 },
                    { (byte)1, 169, 400 },
                    { (byte)2, 169, 400 },
                    { (byte)3, 169, 400 },
                    { (byte)4, 169, 400 },
                    { (byte)5, 169, 400 },
                    { (byte)6, 169, 400 },
                    { (byte)7, 169, 400 },
                    { (byte)8, 169, 400 },
                    { (byte)9, 169, 400 },
                    { (byte)10, 169, 400 },
                    { (byte)11, 169, 400 },
                    { (byte)12, 169, 400 },
                    { (byte)13, 169, 400 },
                    { (byte)14, 169, 400 },
                    { (byte)15, 169, 400 },
                    { (byte)16, 169, 400 },
                    { (byte)17, 169, 400 },
                    { (byte)18, 169, 400 },
                    { (byte)23, 169, 400 },
                    { (byte)25, 169, 400 },
                    { (byte)26, 169, 400 },
                    { (byte)1, 170, 200 },
                    { (byte)2, 170, 200 },
                    { (byte)3, 170, 200 },
                    { (byte)4, 170, 200 },
                    { (byte)5, 170, 200 },
                    { (byte)6, 170, 200 },
                    { (byte)7, 170, 200 },
                    { (byte)8, 170, 200 },
                    { (byte)9, 170, 200 },
                    { (byte)10, 170, 200 },
                    { (byte)11, 170, 200 },
                    { (byte)12, 170, 200 },
                    { (byte)13, 170, 200 },
                    { (byte)14, 170, 200 },
                    { (byte)15, 170, 200 },
                    { (byte)16, 170, 200 },
                    { (byte)17, 170, 200 },
                    { (byte)18, 170, 200 },
                    { (byte)23, 170, 200 },
                    { (byte)25, 170, 200 },
                    { (byte)26, 170, 200 },
                    { (byte)1, 174, 400 },
                    { (byte)2, 174, 400 },
                    { (byte)3, 174, 400 },
                    { (byte)4, 174, 400 },
                    { (byte)5, 174, 400 },
                    { (byte)6, 174, 400 },
                    { (byte)7, 174, 400 },
                    { (byte)8, 174, 400 },
                    { (byte)9, 174, 400 },
                    { (byte)10, 174, 400 },
                    { (byte)11, 174, 400 },
                    { (byte)12, 174, 400 },
                    { (byte)13, 174, 400 },
                    { (byte)14, 174, 400 },
                    { (byte)15, 174, 400 },
                    { (byte)16, 174, 400 },
                    { (byte)17, 174, 400 },
                    { (byte)18, 174, 400 },
                    { (byte)23, 174, 400 },
                    { (byte)25, 174, 400 },
                    { (byte)26, 174, 400 },
                    { (byte)1, 175, 200 },
                    { (byte)2, 175, 200 },
                    { (byte)3, 175, 200 },
                    { (byte)4, 175, 200 },
                    { (byte)5, 175, 200 },
                    { (byte)6, 175, 200 },
                    { (byte)7, 175, 200 },
                    { (byte)8, 175, 200 },
                    { (byte)9, 175, 200 },
                    { (byte)10, 175, 200 },
                    { (byte)11, 175, 200 },
                    { (byte)12, 175, 200 },
                    { (byte)13, 175, 200 },
                    { (byte)14, 175, 200 },
                    { (byte)15, 175, 200 },
                    { (byte)16, 175, 200 },
                    { (byte)17, 175, 200 },
                    { (byte)18, 175, 200 },
                    { (byte)23, 175, 200 },
                    { (byte)25, 175, 200 },
                    { (byte)26, 175, 200 },
                    { (byte)1, 179, 400 },
                    { (byte)2, 179, 400 },
                    { (byte)3, 179, 400 },
                    { (byte)4, 179, 400 },
                    { (byte)5, 179, 400 },
                    { (byte)6, 179, 400 },
                    { (byte)7, 179, 400 },
                    { (byte)8, 179, 400 },
                    { (byte)9, 179, 400 },
                    { (byte)10, 179, 400 },
                    { (byte)11, 179, 400 },
                    { (byte)12, 179, 400 },
                    { (byte)13, 179, 400 },
                    { (byte)14, 179, 400 },
                    { (byte)15, 179, 400 },
                    { (byte)16, 179, 400 },
                    { (byte)17, 179, 400 },
                    { (byte)18, 179, 400 },
                    { (byte)23, 179, 400 },
                    { (byte)25, 179, 400 },
                    { (byte)26, 179, 400 },
                    { (byte)1, 180, 200 },
                    { (byte)2, 180, 200 },
                    { (byte)3, 180, 200 },
                    { (byte)4, 180, 200 },
                    { (byte)5, 180, 200 },
                    { (byte)6, 180, 200 },
                    { (byte)7, 180, 200 },
                    { (byte)8, 180, 200 },
                    { (byte)9, 180, 200 },
                    { (byte)10, 180, 200 },
                    { (byte)11, 180, 200 },
                    { (byte)12, 180, 200 },
                    { (byte)13, 180, 200 },
                    { (byte)14, 180, 200 },
                    { (byte)15, 180, 200 },
                    { (byte)16, 180, 200 },
                    { (byte)17, 180, 200 },
                    { (byte)18, 180, 200 },
                    { (byte)23, 180, 200 },
                    { (byte)25, 180, 200 },
                    { (byte)26, 180, 200 },
                    { (byte)19, 184, 400 },
                    { (byte)20, 184, 400 },
                    { (byte)21, 184, 400 },
                    { (byte)22, 184, 400 },
                    { (byte)19, 185, 200 },
                    { (byte)20, 185, 200 },
                    { (byte)21, 185, 200 },
                    { (byte)22, 185, 200 },
                    { (byte)4, 189, 400 },
                    { (byte)10, 189, 400 },
                    { (byte)16, 189, 400 },
                    { (byte)26, 189, 400 },
                    { (byte)20, 193, 400 },
                    { (byte)21, 193, 400 },
                    { (byte)24, 193, 400 },
                    { (byte)25, 193, 400 },
                    { (byte)26, 193, 400 },
                    { (byte)20, 194, 200 },
                    { (byte)21, 194, 200 },
                    { (byte)24, 194, 200 },
                    { (byte)25, 194, 200 },
                    { (byte)26, 194, 200 },
                    { (byte)19, 198, 400 },
                    { (byte)20, 198, 400 },
                    { (byte)21, 198, 400 },
                    { (byte)22, 198, 400 },
                    { (byte)24, 198, 400 },
                    { (byte)25, 198, 400 },
                    { (byte)26, 198, 400 },
                    { (byte)19, 199, 200 },
                    { (byte)20, 199, 200 },
                    { (byte)21, 199, 200 },
                    { (byte)22, 199, 200 },
                    { (byte)24, 199, 200 },
                    { (byte)25, 199, 200 },
                    { (byte)26, 199, 200 },
                    { (byte)19, 203, 400 },
                    { (byte)20, 203, 400 },
                    { (byte)21, 203, 400 },
                    { (byte)22, 203, 400 },
                    { (byte)25, 203, 400 },
                    { (byte)19, 204, 200 },
                    { (byte)20, 204, 200 },
                    { (byte)21, 204, 200 },
                    { (byte)22, 204, 200 },
                    { (byte)25, 204, 200 },
                    { (byte)6, 208, 400 },
                    { (byte)12, 208, 400 },
                    { (byte)18, 208, 400 },
                    { (byte)6, 209, 200 },
                    { (byte)12, 209, 200 },
                    { (byte)18, 209, 200 },
                    { (byte)1, 213, 400 },
                    { (byte)2, 213, 400 },
                    { (byte)7, 213, 400 },
                    { (byte)8, 213, 400 },
                    { (byte)13, 213, 400 },
                    { (byte)14, 213, 400 },
                    { (byte)25, 213, 400 },
                    { (byte)1, 214, 200 },
                    { (byte)2, 214, 200 },
                    { (byte)7, 214, 200 },
                    { (byte)8, 214, 200 },
                    { (byte)13, 214, 200 },
                    { (byte)14, 214, 200 },
                    { (byte)25, 214, 200 },
                    { (byte)23, 218, 400 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemModifiers_Tier",
                table: "ItemModifiers",
                sql: "\"Tier\" IS NULL OR \"Tier\" BETWEEN 1 AND 5");
        }
    }
}
