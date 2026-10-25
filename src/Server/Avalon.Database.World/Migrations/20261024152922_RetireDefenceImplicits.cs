using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #1179 (part B2): a base's defence and block are its base stats now (AddItemTemplateBaseStats), so Part B's 60
    /// per-base implicits retire: their template links, stat lines, rows 224-283 and groups 50-109. Only owned copies wiped
    /// before this part (Character WipeOwnedEquipmentForBaseStats) named them; trade records may still, so the ids are
    /// never reused. Down puts them back as they were.
    /// </summary>
    public partial class RetireDefenceImplicits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 224 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 225 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 226 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 227 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 228 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 229 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 230 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 231 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 232 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 233 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 234 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 235 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 236 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 237 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 238 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 239 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 240 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 241 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 242 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 243 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 244 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 245 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 246 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 247 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 248 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 249 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 250 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 251 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 252 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 253 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 254 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 254 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 255 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 255 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 256 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 256 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 257 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 257 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 258 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 258 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 259 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)1, 259 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 260 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 261 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 262 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 263 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 264 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 265 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 266 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 267 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 268 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 269 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 270 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 271 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 272 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 273 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 274 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 275 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 276 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 277 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 278 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 279 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 280 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 281 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 282 });

            migrationBuilder.DeleteData(
                table: "ItemModifierStats",
                keyColumns: new[] { "Index", "ModifierId" },
                keyValues: new object[] { (byte)0, 283 });

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
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 224);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 225);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 226);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 227);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 228);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 229);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 230);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 231);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 232);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 233);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 234);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 235);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 236);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 237);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 238);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 239);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 240);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 241);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 242);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 243);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 244);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 245);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 246);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 247);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 248);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 249);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 250);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 251);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 252);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 253);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 254);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 255);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 256);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 257);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 258);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 259);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 260);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 261);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 262);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 263);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 264);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 265);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 266);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 267);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 268);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 269);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 270);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 271);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 272);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 273);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 274);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 275);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 276);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 277);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 278);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 279);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 280);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 281);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 282);

            migrationBuilder.DeleteData(
                table: "ItemModifiers",
                keyColumn: "Id",
                keyValue: 283);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 75);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 76);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 77);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 78);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 79);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 81);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 82);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 83);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 84);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 85);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 86);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 87);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 88);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 89);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 90);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 91);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 92);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 93);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 94);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 95);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 96);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 97);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 98);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 99);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 101);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 102);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 103);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 106);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 107);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 108);

            migrationBuilder.DeleteData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 109);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemModifierGroups",
                columns: new[] { "Id", "Code", "Element", "Kind", "Name" },
                values: new object[,]
                {
                    { 50, null, null, (byte)4, "Barkplate Helm" },
                    { 51, null, null, (byte)4, "Barkplate Chestguard" },
                    { 52, null, null, (byte)4, "Barkplate Legguards" },
                    { 53, null, null, (byte)4, "Barkplate Gauntlets" },
                    { 54, null, null, (byte)4, "Barkplate Boots" },
                    { 55, null, null, (byte)4, "Mossweave Hood" },
                    { 56, null, null, (byte)4, "Mossweave Robe" },
                    { 57, null, null, (byte)4, "Mossweave Leggings" },
                    { 58, null, null, (byte)4, "Mossweave Gloves" },
                    { 59, null, null, (byte)4, "Mossweave Slippers" },
                    { 60, null, null, (byte)4, "Fernstalker Cap" },
                    { 61, null, null, (byte)4, "Fernstalker Jerkin" },
                    { 62, null, null, (byte)4, "Fernstalker Breeches" },
                    { 63, null, null, (byte)4, "Fernstalker Grips" },
                    { 64, null, null, (byte)4, "Fernstalker Boots" },
                    { 65, null, null, (byte)4, "Ironbound Helm" },
                    { 66, null, null, (byte)4, "Ironbound Chestguard" },
                    { 67, null, null, (byte)4, "Ironbound Legguards" },
                    { 68, null, null, (byte)4, "Ironbound Gauntlets" },
                    { 69, null, null, (byte)4, "Ironbound Boots" },
                    { 70, null, null, (byte)4, "Linen Hood" },
                    { 71, null, null, (byte)4, "Linen Robe" },
                    { 72, null, null, (byte)4, "Linen Leggings" },
                    { 73, null, null, (byte)4, "Linen Gloves" },
                    { 74, null, null, (byte)4, "Linen Slippers" },
                    { 75, null, null, (byte)4, "Hide Cap" },
                    { 76, null, null, (byte)4, "Hide Jerkin" },
                    { 77, null, null, (byte)4, "Hide Breeches" },
                    { 78, null, null, (byte)4, "Hide Grips" },
                    { 79, null, null, (byte)4, "Hide Boots" },
                    { 80, null, null, (byte)4, "Oak Buckler" },
                    { 81, null, null, (byte)4, "Barkplate Shield" },
                    { 82, null, null, (byte)4, "Ironroot Kite Shield" },
                    { 83, null, null, (byte)4, "Ash Focus" },
                    { 84, null, null, (byte)4, "Thornwood Focus" },
                    { 85, null, null, (byte)4, "Gloamwood Orb" },
                    { 86, null, null, (byte)4, "Ironbound Pauldrons" },
                    { 87, null, null, (byte)4, "Hide Mantle" },
                    { 88, null, null, (byte)4, "Linen Shoulderwraps" },
                    { 89, null, null, (byte)4, "Barkplate Pauldrons" },
                    { 90, null, null, (byte)4, "Fernstalker Mantle" },
                    { 91, null, null, (byte)4, "Mossweave Shoulderwraps" },
                    { 92, null, null, (byte)4, "Ironroot Pauldrons" },
                    { 93, null, null, (byte)4, "Gloamhide Mantle" },
                    { 94, null, null, (byte)4, "Duskweave Shoulderwraps" },
                    { 95, null, null, (byte)4, "Ironroot Helm" },
                    { 96, null, null, (byte)4, "Ironroot Chestguard" },
                    { 97, null, null, (byte)4, "Ironroot Legguards" },
                    { 98, null, null, (byte)4, "Ironroot Gauntlets" },
                    { 99, null, null, (byte)4, "Ironroot Boots" },
                    { 100, null, null, (byte)4, "Gloamhide Cap" },
                    { 101, null, null, (byte)4, "Gloamhide Jerkin" },
                    { 102, null, null, (byte)4, "Gloamhide Breeches" },
                    { 103, null, null, (byte)4, "Gloamhide Grips" },
                    { 104, null, null, (byte)4, "Gloamhide Boots" },
                    { 105, null, null, (byte)4, "Duskweave Hood" },
                    { 106, null, null, (byte)4, "Duskweave Robe" },
                    { 107, null, null, (byte)4, "Duskweave Leggings" },
                    { 108, null, null, (byte)4, "Duskweave Gloves" },
                    { 109, null, null, (byte)4, "Duskweave Slippers" }
                });

            migrationBuilder.InsertData(
                table: "ItemModifiers",
                columns: new[] { "Id", "GroupId", "RequiredItemLevel", "Tier" },
                values: new object[,]
                {
                    { 224, 50, (short)1, null },
                    { 225, 51, (short)1, null },
                    { 226, 52, (short)1, null },
                    { 227, 53, (short)1, null },
                    { 228, 54, (short)1, null },
                    { 229, 55, (short)1, null },
                    { 230, 56, (short)1, null },
                    { 231, 57, (short)1, null },
                    { 232, 58, (short)1, null },
                    { 233, 59, (short)1, null },
                    { 234, 60, (short)1, null },
                    { 235, 61, (short)1, null },
                    { 236, 62, (short)1, null },
                    { 237, 63, (short)1, null },
                    { 238, 64, (short)1, null },
                    { 239, 65, (short)1, null },
                    { 240, 66, (short)1, null },
                    { 241, 67, (short)1, null },
                    { 242, 68, (short)1, null },
                    { 243, 69, (short)1, null },
                    { 244, 70, (short)1, null },
                    { 245, 71, (short)1, null },
                    { 246, 72, (short)1, null },
                    { 247, 73, (short)1, null },
                    { 248, 74, (short)1, null },
                    { 249, 75, (short)1, null },
                    { 250, 76, (short)1, null },
                    { 251, 77, (short)1, null },
                    { 252, 78, (short)1, null },
                    { 253, 79, (short)1, null },
                    { 254, 80, (short)1, null },
                    { 255, 81, (short)1, null },
                    { 256, 82, (short)1, null },
                    { 257, 83, (short)1, null },
                    { 258, 84, (short)1, null },
                    { 259, 85, (short)1, null },
                    { 260, 86, (short)1, null },
                    { 261, 87, (short)1, null },
                    { 262, 88, (short)1, null },
                    { 263, 89, (short)1, null },
                    { 264, 90, (short)1, null },
                    { 265, 91, (short)1, null },
                    { 266, 92, (short)1, null },
                    { 267, 93, (short)1, null },
                    { 268, 94, (short)1, null },
                    { 269, 95, (short)1, null },
                    { 270, 96, (short)1, null },
                    { 271, 97, (short)1, null },
                    { 272, 98, (short)1, null },
                    { 273, 99, (short)1, null },
                    { 274, 100, (short)1, null },
                    { 275, 101, (short)1, null },
                    { 276, 102, (short)1, null },
                    { 277, 103, (short)1, null },
                    { 278, 104, (short)1, null },
                    { 279, 105, (short)1, null },
                    { 280, 106, (short)1, null },
                    { 281, 107, (short)1, null },
                    { 282, 108, (short)1, null },
                    { 283, 109, (short)1, null }
                });

            migrationBuilder.InsertData(
                table: "ItemModifierStats",
                columns: new[] { "Index", "ModifierId", "Max", "Max2", "Min", "Min2", "Stat" },
                values: new object[,]
                {
                    { (byte)0, 224, 13, null, 9, null, (short)4 },
                    { (byte)0, 225, 21, null, 15, null, (short)4 },
                    { (byte)0, 226, 16, null, 12, null, (short)4 },
                    { (byte)0, 227, 8, null, 6, null, (short)4 },
                    { (byte)0, 228, 8, null, 6, null, (short)4 },
                    { (byte)0, 229, 18, null, 14, null, (short)14 },
                    { (byte)0, 230, 30, null, 22, null, (short)14 },
                    { (byte)0, 231, 24, null, 18, null, (short)14 },
                    { (byte)0, 232, 12, null, 9, null, (short)14 },
                    { (byte)0, 233, 12, null, 9, null, (short)14 },
                    { (byte)0, 234, 62, null, 46, null, (short)6 },
                    { (byte)0, 235, 104, null, 77, null, (short)6 },
                    { (byte)0, 236, 83, null, 61, null, (short)6 },
                    { (byte)0, 237, 41, null, 31, null, (short)6 },
                    { (byte)0, 238, 41, null, 31, null, (short)6 },
                    { (byte)0, 239, 6, null, 4, null, (short)4 },
                    { (byte)0, 240, 9, null, 7, null, (short)4 },
                    { (byte)0, 241, 7, null, 5, null, (short)4 },
                    { (byte)0, 242, 3, null, 3, null, (short)4 },
                    { (byte)0, 243, 3, null, 3, null, (short)4 },
                    { (byte)0, 244, 8, null, 6, null, (short)14 },
                    { (byte)0, 245, 14, null, 10, null, (short)14 },
                    { (byte)0, 246, 12, null, 9, null, (short)14 },
                    { (byte)0, 247, 6, null, 4, null, (short)14 },
                    { (byte)0, 248, 6, null, 4, null, (short)14 },
                    { (byte)0, 249, 28, null, 20, null, (short)6 },
                    { (byte)0, 250, 46, null, 34, null, (short)6 },
                    { (byte)0, 251, 37, null, 27, null, (short)6 },
                    { (byte)0, 252, 18, null, 14, null, (short)6 },
                    { (byte)0, 253, 18, null, 14, null, (short)6 },
                    { (byte)0, 254, 7, null, 5, null, (short)4 },
                    { (byte)1, 254, 6, null, 4, null, (short)5 },
                    { (byte)0, 255, 16, null, 12, null, (short)4 },
                    { (byte)1, 255, 8, null, 6, null, (short)5 },
                    { (byte)0, 256, 28, null, 20, null, (short)4 },
                    { (byte)1, 256, 12, null, 9, null, (short)5 },
                    { (byte)0, 257, 9, null, 7, null, (short)14 },
                    { (byte)1, 257, 6, null, 4, null, (short)26 },
                    { (byte)0, 258, 21, null, 15, null, (short)14 },
                    { (byte)1, 258, 9, null, 7, null, (short)26 },
                    { (byte)0, 259, 35, null, 26, null, (short)14 },
                    { (byte)1, 259, 14, null, 10, null, (short)26 },
                    { (byte)0, 260, 5, null, 3, null, (short)4 },
                    { (byte)0, 261, 23, null, 17, null, (short)6 },
                    { (byte)0, 262, 7, null, 5, null, (short)14 },
                    { (byte)0, 263, 10, null, 8, null, (short)4 },
                    { (byte)0, 264, 52, null, 38, null, (short)6 },
                    { (byte)0, 265, 15, null, 11, null, (short)14 },
                    { (byte)0, 266, 18, null, 14, null, (short)4 },
                    { (byte)0, 267, 92, null, 68, null, (short)6 },
                    { (byte)0, 268, 26, null, 20, null, (short)14 },
                    { (byte)0, 269, 22, null, 16, null, (short)4 },
                    { (byte)0, 270, 37, null, 27, null, (short)4 },
                    { (byte)0, 271, 30, null, 22, null, (short)4 },
                    { (byte)0, 272, 15, null, 11, null, (short)4 },
                    { (byte)0, 273, 15, null, 11, null, (short)4 },
                    { (byte)0, 274, 110, null, 82, null, (short)6 },
                    { (byte)0, 275, 184, null, 136, null, (short)6 },
                    { (byte)0, 276, 147, null, 109, null, (short)6 },
                    { (byte)0, 277, 74, null, 54, null, (short)6 },
                    { (byte)0, 278, 74, null, 54, null, (short)6 },
                    { (byte)0, 279, 31, null, 23, null, (short)14 },
                    { (byte)0, 280, 52, null, 38, null, (short)14 },
                    { (byte)0, 281, 41, null, 31, null, (short)14 },
                    { (byte)0, 282, 21, null, 15, null, (short)14 },
                    { (byte)0, 283, 21, null, 15, null, (short)14 }
                });

            migrationBuilder.InsertData(
                table: "ItemTemplateModifiers",
                columns: new[] { "Index", "TemplateId", "ModifierId" },
                values: new object[,]
                {
                    { (byte)0, 12m, 224 },
                    { (byte)0, 13m, 225 },
                    { (byte)0, 14m, 226 },
                    { (byte)0, 15m, 227 },
                    { (byte)0, 16m, 228 },
                    { (byte)0, 17m, 229 },
                    { (byte)0, 18m, 230 },
                    { (byte)0, 19m, 231 },
                    { (byte)0, 20m, 232 },
                    { (byte)0, 21m, 233 },
                    { (byte)0, 22m, 234 },
                    { (byte)0, 23m, 235 },
                    { (byte)0, 24m, 236 },
                    { (byte)0, 25m, 237 },
                    { (byte)0, 26m, 238 },
                    { (byte)0, 36m, 239 },
                    { (byte)0, 37m, 240 },
                    { (byte)0, 38m, 241 },
                    { (byte)0, 39m, 242 },
                    { (byte)0, 40m, 243 },
                    { (byte)0, 41m, 244 },
                    { (byte)0, 42m, 245 },
                    { (byte)0, 43m, 246 },
                    { (byte)0, 44m, 247 },
                    { (byte)0, 45m, 248 },
                    { (byte)0, 46m, 249 },
                    { (byte)0, 47m, 250 },
                    { (byte)0, 48m, 251 },
                    { (byte)0, 49m, 252 },
                    { (byte)0, 50m, 253 },
                    { (byte)0, 70m, 254 },
                    { (byte)0, 71m, 255 },
                    { (byte)0, 72m, 256 },
                    { (byte)0, 73m, 257 },
                    { (byte)0, 74m, 258 },
                    { (byte)0, 75m, 259 },
                    { (byte)0, 76m, 260 },
                    { (byte)0, 77m, 261 },
                    { (byte)0, 78m, 262 },
                    { (byte)0, 79m, 263 },
                    { (byte)0, 80m, 264 },
                    { (byte)0, 81m, 265 },
                    { (byte)0, 82m, 266 },
                    { (byte)0, 83m, 267 },
                    { (byte)0, 84m, 268 },
                    { (byte)0, 85m, 269 },
                    { (byte)0, 86m, 270 },
                    { (byte)0, 87m, 271 },
                    { (byte)0, 88m, 272 },
                    { (byte)0, 89m, 273 },
                    { (byte)0, 90m, 274 },
                    { (byte)0, 91m, 275 },
                    { (byte)0, 92m, 276 },
                    { (byte)0, 93m, 277 },
                    { (byte)0, 94m, 278 },
                    { (byte)0, 95m, 279 },
                    { (byte)0, 96m, 280 },
                    { (byte)0, 97m, 281 },
                    { (byte)0, 98m, 282 },
                    { (byte)0, 99m, 283 }
                });
        }
    }
}
