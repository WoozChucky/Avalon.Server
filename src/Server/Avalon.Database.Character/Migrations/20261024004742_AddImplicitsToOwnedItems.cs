using System.Globalization;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <summary>
    /// #1179 (B-R13): a base's defence moved from its template to its implicit modifier (the World migration
    /// MoveBaseStatsToImplicits), so every copy of one of the 60 defence-bearing bases owned before then gets its implicit
    /// here: one ItemInstanceModifiers row (its base's implicit modifier, ordinal 0) and a value row per line at the
    /// drafted value, the defence the copy had, so nothing loses any. Copies made from then on roll theirs when made. No
    /// copy had modifier rows before this part, so nothing collides. Row ids come from gen_random_uuid(): this data
    /// migration is the one place the database makes a modifier id. Down deletes every modifier row naming one of the
    /// table's modifiers (their values go by cascade); the Character database cannot read the World's catalog, so the table
    /// is the list. No model change.
    /// </summary>
    public partial class AddImplicitsToOwnedItems : Migration
    {
        /// <summary>
        /// Each base's implicit as drafted: its template, its implicit modifier's seeded id, and each line's index, stat and
        /// drafted value, in template and line order. Frozen copies of ItemModifierSeed's values (a migration never reads
        /// the seed, which is another database's); ImplicitBackfillShould holds them equal.
        /// </summary>
        public static readonly (ulong TemplateId, int ModifierId, byte Index, StatType Stat, int Value)[] Implicits =
        [
            (12, 224, 0, StatType.Armor, 11),
            (13, 225, 0, StatType.Armor, 18),
            (14, 226, 0, StatType.Armor, 14),
            (15, 227, 0, StatType.Armor, 7),
            (16, 228, 0, StatType.Armor, 7),
            (17, 229, 0, StatType.Ward, 16),
            (18, 230, 0, StatType.Ward, 26),
            (19, 231, 0, StatType.Ward, 21),
            (20, 232, 0, StatType.Ward, 10),
            (21, 233, 0, StatType.Ward, 10),
            (22, 234, 0, StatType.Evasion, 54),
            (23, 235, 0, StatType.Evasion, 90),
            (24, 236, 0, StatType.Evasion, 72),
            (25, 237, 0, StatType.Evasion, 36),
            (26, 238, 0, StatType.Evasion, 36),
            (36, 239, 0, StatType.Armor, 5),
            (37, 240, 0, StatType.Armor, 8),
            (38, 241, 0, StatType.Armor, 6),
            (39, 242, 0, StatType.Armor, 3),
            (40, 243, 0, StatType.Armor, 3),
            (41, 244, 0, StatType.Ward, 7),
            (42, 245, 0, StatType.Ward, 12),
            (43, 246, 0, StatType.Ward, 10),
            (44, 247, 0, StatType.Ward, 5),
            (45, 248, 0, StatType.Ward, 5),
            (46, 249, 0, StatType.Evasion, 24),
            (47, 250, 0, StatType.Evasion, 40),
            (48, 251, 0, StatType.Evasion, 32),
            (49, 252, 0, StatType.Evasion, 16),
            (50, 253, 0, StatType.Evasion, 16),
            (70, 254, 0, StatType.Armor, 6),
            (70, 254, 1, StatType.BlockPct, 5),
            (71, 255, 0, StatType.Armor, 14),
            (71, 255, 1, StatType.BlockPct, 7),
            (72, 256, 0, StatType.Armor, 24),
            (72, 256, 1, StatType.BlockPct, 10),
            (73, 257, 0, StatType.Ward, 8),
            (73, 257, 1, StatType.IncreasedSpellDamage, 5),
            (74, 258, 0, StatType.Ward, 18),
            (74, 258, 1, StatType.IncreasedSpellDamage, 8),
            (75, 259, 0, StatType.Ward, 30),
            (75, 259, 1, StatType.IncreasedSpellDamage, 12),
            (76, 260, 0, StatType.Armor, 4),
            (77, 261, 0, StatType.Evasion, 20),
            (78, 262, 0, StatType.Ward, 6),
            (79, 263, 0, StatType.Armor, 9),
            (80, 264, 0, StatType.Evasion, 45),
            (81, 265, 0, StatType.Ward, 13),
            (82, 266, 0, StatType.Armor, 16),
            (83, 267, 0, StatType.Evasion, 80),
            (84, 268, 0, StatType.Ward, 23),
            (85, 269, 0, StatType.Armor, 19),
            (86, 270, 0, StatType.Armor, 32),
            (87, 271, 0, StatType.Armor, 26),
            (88, 272, 0, StatType.Armor, 13),
            (89, 273, 0, StatType.Armor, 13),
            (90, 274, 0, StatType.Evasion, 96),
            (91, 275, 0, StatType.Evasion, 160),
            (92, 276, 0, StatType.Evasion, 128),
            (93, 277, 0, StatType.Evasion, 64),
            (94, 278, 0, StatType.Evasion, 64),
            (95, 279, 0, StatType.Ward, 27),
            (96, 280, 0, StatType.Ward, 45),
            (97, 281, 0, StatType.Ward, 36),
            (98, 282, 0, StatType.Ward, 18),
            (99, 283, 0, StatType.Ward, 18),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            string implicits = string.Join(", ", Implicits.Select(row => string.Create(CultureInfo.InvariantCulture,
                $"({row.TemplateId}, {row.ModifierId}, {row.Index}, {(int)row.Stat}, {row.Value})")));

            // One statement: the modifier rows are inserted for every owned copy of a listed base, and their value rows
            // from what that insert returns, joined back to the table by the copy's template.
            migrationBuilder.Sql($"""
                WITH "Implicit" ("TemplateId", "ModifierId", "Index", "Stat", "Value") AS (VALUES
                    {implicits}),
                "Added" AS (
                    INSERT INTO "ItemInstanceModifiers" ("Id", "ItemInstanceId", "ModifierId", "Ordinal")
                    SELECT gen_random_uuid(), i."Id", b."ModifierId", 0
                    FROM "ItemInstances" AS i
                    JOIN (SELECT DISTINCT "TemplateId", "ModifierId" FROM "Implicit") AS b ON b."TemplateId" = i."TemplateId"
                    RETURNING "Id", "ItemInstanceId", "ModifierId")
                INSERT INTO "ItemInstanceModifierValues" ("ItemInstanceModifierId", "Index", "Stat", "Value", "Value2")
                SELECT a."Id", v."Index", v."Stat", v."Value", NULL
                FROM "Added" AS a
                JOIN "ItemInstances" AS i ON i."Id" = a."ItemInstanceId"
                JOIN "Implicit" AS v ON v."TemplateId" = i."TemplateId" AND v."ModifierId" = a."ModifierId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string modifiers = string.Join(", ", Implicits.Select(row => row.ModifierId).Distinct()
                .Select(id => id.ToString(CultureInfo.InvariantCulture)));
            migrationBuilder.Sql($"DELETE FROM \"ItemInstanceModifiers\" WHERE \"ModifierId\" IN ({modifiers});");
        }
    }
}
